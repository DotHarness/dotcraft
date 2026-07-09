import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, within } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { PerforcePrepareDialog } from '../components/detail/PerforcePrepareDialog'
import { useConversationStore } from '../stores/conversationStore'

const settingsGet = vi.fn()

function renderDialog(onPrepare = vi.fn()): ReturnType<typeof vi.fn> {
  useConversationStore.getState().upsertChangedFile({
    filePath: 'C:\\workspace\\sample-app\\src\\a.ts',
    turnId: 'turn-1',
    turnIds: ['turn-1'],
    additions: 2,
    deletions: 1,
    diffHunks: [],
    status: 'written',
    isNewFile: false
  })

  render(
    <LocaleProvider>
      <PerforcePrepareDialog
        workspacePath="C:\\workspace\\sample-app"
        changelist="123"
        changelists={[
          { id: 'default', isDefault: true, description: 'Default changelist', user: 'me', client: 'ws', status: 'pending' },
          { id: '123', isDefault: false, description: 'Task CL', user: 'me', client: 'ws', status: 'pending' },
          { id: '456', isDefault: false, description: 'Other CL', user: 'me', client: 'ws', status: 'pending' }
        ]}
        onPrepare={onPrepare}
        onClose={vi.fn()}
      />
    </LocaleProvider>
  )

  return onPrepare
}

describe('PerforcePrepareDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConversationStore.getState().reset()
    settingsGet.mockResolvedValue({ locale: 'en' })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet }
      }
    })
  })

  it('submits the selected numbered changelist target', () => {
    const onPrepare = renderDialog()

    const targetSelect = screen.getByRole('combobox', { name: 'Target' })
    const option = within(targetSelect).getByRole('option', { name: '456 - Other CL' })
    fireEvent.change(targetSelect, { target: { value: option.getAttribute('value') } })
    fireEvent.change(screen.getByPlaceholderText('Leave blank to auto-generate changelist description'), {
      target: { value: 'Prepare existing CL' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Checkout' }))

    expect(onPrepare).toHaveBeenCalledWith('Prepare existing CL', '456')
  })

  it('maps New Changelist to the default prepare target', () => {
    const onPrepare = renderDialog()

    const targetSelect = screen.getByRole('combobox', { name: 'Target' })
    const option = within(targetSelect).getByRole('option', { name: 'New Changelist' })
    fireEvent.change(targetSelect, { target: { value: option.getAttribute('value') } })
    fireEvent.click(screen.getByRole('button', { name: 'Checkout' }))

    expect(onPrepare).toHaveBeenCalledWith('', 'default')
  })
})

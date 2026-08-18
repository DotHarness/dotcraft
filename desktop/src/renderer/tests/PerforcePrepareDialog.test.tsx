import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { PerforcePrepareDialog } from '../components/detail/PerforcePrepareDialog'
import { useConversationStore } from '../stores/conversationStore'
import { installDesktopApiMock } from './desktopApiMock'

const settingsGet = vi.fn()

function renderDialog(onPrepare = vi.fn(), onClose = vi.fn()): ReturnType<typeof vi.fn> {
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
        onClose={onClose}
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
    installDesktopApiMock({ settings: { get: settingsGet } })
  })

  it('submits the selected numbered changelist target', () => {
    const onPrepare = renderDialog()

    const targetSelect = screen.getByRole('combobox', { name: 'Target' })
    fireEvent.click(targetSelect)
    fireEvent.click(screen.getByRole('option', { name: '456 - Other CL' }))
    fireEvent.change(screen.getByPlaceholderText('Leave blank to auto-generate changelist description'), {
      target: { value: 'Prepare existing CL' }
    })
    fireEvent.click(screen.getByRole('button', { name: 'Checkout' }))

    expect(onPrepare).toHaveBeenCalledWith('Prepare existing CL', '456')
  })

  it('maps New Changelist to the default prepare target', () => {
    const onPrepare = renderDialog()

    const targetSelect = screen.getByRole('combobox', { name: 'Target' })
    fireEvent.click(targetSelect)
    fireEvent.click(screen.getByRole('option', { name: 'New Changelist' }))
    fireEvent.click(screen.getByRole('button', { name: 'Checkout' }))

    expect(onPrepare).toHaveBeenCalledWith('', 'default')
  })

  it('supports keyboard selection and closes only the selector on Escape', () => {
    const onPrepare = vi.fn()
    const onClose = vi.fn()
    renderDialog(onPrepare, onClose)

    const targetSelect = screen.getByRole('combobox', { name: 'Target' })
    fireEvent.keyDown(targetSelect, { key: 'ArrowDown' })
    fireEvent.keyDown(targetSelect, { key: 'ArrowDown' })
    fireEvent.keyDown(targetSelect, { key: 'Enter' })

    fireEvent.click(targetSelect)
    expect(screen.getByRole('listbox', { name: 'Target' })).toBeInTheDocument()
    fireEvent.keyDown(targetSelect, { key: 'Escape' })

    expect(screen.queryByRole('listbox', { name: 'Target' })).not.toBeInTheDocument()
    expect(onClose).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Checkout' }))
    expect(onPrepare).toHaveBeenCalledWith('', '456')
  })
})

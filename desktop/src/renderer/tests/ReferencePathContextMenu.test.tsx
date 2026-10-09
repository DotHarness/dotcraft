import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ReferencePathContextMenu } from '../components/conversation/ReferencePathContextMenu'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useComposerFileReferenceStore } from '../stores/composerFileReferenceStore'
import { installDesktopApiMock } from './desktopApiMock'

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const listEditors = vi.fn()

describe('ReferencePathContextMenu', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    settingsSet.mockResolvedValue(undefined)
    listEditors.mockResolvedValue([
      {
        id: 'explorer',
        labelKey: 'editors.explorer',
        iconKey: 'explorer'
      }
    ])
    useComposerFileReferenceStore.setState({ pendingByScope: new Map() })
    installDesktopApiMock({
      settings: {
        get: settingsGet,
        set: settingsSet
      },
      shell: {
        listEditors,
        revealLocalPath: vi.fn(),
        launchLocalPathInEditor: vi.fn(),
        openLocalPath: vi.fn()
      }
    })
  })

  it('requests an inline file reference for the active composer', async () => {
    const onClose = vi.fn()
    render(
      <LocaleProvider>
        <ReferencePathContextMenu
          position={{ x: 20, y: 20 }}
          targetPath={'C:\\sample\\workspace\\.dockerignore'}
          onClose={onClose}
        />
      </LocaleProvider>
    )
    await act(async () => {
      await Promise.resolve()
      await Promise.resolve()
    })

    expect(screen.queryByRole('menuitem', { name: 'Add as attachment' })).toBeNull()
    fireEvent.click(screen.getByRole('menuitem', { name: 'Add to chat' }))

    expect(useComposerFileReferenceStore.getState().pendingByScope.get(null)).toEqual([
      'C:\\sample\\workspace\\.dockerignore'
    ])
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('omits Add to chat for directory targets', async () => {
    render(
      <LocaleProvider>
        <ReferencePathContextMenu
          position={{ x: 20, y: 20 }}
          targetPath={'C:\\sample\\workspace\\src'}
          allowAddToChat={false}
          onClose={vi.fn()}
        />
      </LocaleProvider>
    )
    await act(async () => {
      await Promise.resolve()
      await Promise.resolve()
    })

    expect(screen.queryByRole('menuitem', { name: 'Add to chat' })).toBeNull()
    expect(screen.queryByRole('menuitem', { name: 'Add as attachment' })).toBeNull()
  })
})

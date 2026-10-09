import { act, fireEvent, render, screen } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ReferencePathContextMenu } from '../components/conversation/ReferencePathContextMenu'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useUIStore } from '../stores/uiStore'
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
    useUIStore.setState({ composerFileReferenceRequest: null, composerFileAttachmentRequest: null })
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

  it('requests a file reference without queuing an attachment', async () => {
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

    fireEvent.click(screen.getByRole('menuitem', { name: 'Add to chat' }))

    expect(useUIStore.getState().composerFileReferenceRequest).toEqual({
      scopeId: null,
      paths: ['C:\\sample\\workspace\\.dockerignore']
    })
    expect(useUIStore.getState().composerFileAttachmentRequest).toBeNull()
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('queues an attachment without requesting a file reference', async () => {
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

    fireEvent.click(screen.getByRole('menuitem', { name: 'Add as attachment' }))

    expect(useUIStore.getState().composerFileAttachmentRequest?.file).toEqual({
      path: 'C:\\sample\\workspace\\.dockerignore',
      fileName: '.dockerignore'
    })
    expect(useUIStore.getState().composerFileReferenceRequest).toBeNull()
    expect(onClose).toHaveBeenCalledOnce()
  })

  it('omits both composer actions for directory targets', async () => {
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

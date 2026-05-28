import { describe, expect, it, vi, beforeEach } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { AttachmentStrip } from '../components/conversation/AttachmentStrip'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'

const settingsGet = vi.fn()
const authorizeFile = vi.fn()
const classify = vi.fn()

function renderWithLocale(ui: JSX.Element): ReturnType<typeof render> {
  return render(<LocaleProvider>{ui}</LocaleProvider>)
}

describe('AttachmentStrip', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ locale: 'en' })
    authorizeFile.mockImplementation(async ({ absolutePath }: { absolutePath: string }) => ({ absolutePath }))
    classify.mockImplementation(async ({ absolutePath }: { absolutePath: string }) => ({
      contentClass: absolutePath.endsWith('.png') ? 'image' : 'text',
      mime: absolutePath.endsWith('.png') ? 'image/png' : 'text/plain',
      sizeBytes: 12
    }))
    useConversationStore.getState().reset()
    useConversationStore.setState({ workspacePath: 'F:/workspace' })
    useThreadStore.setState({ activeThreadId: 'thread-1' })
    useViewerTabStore.setState({
      byThread: new Map(),
      currentThreadId: 'thread-1',
      currentWorkspacePath: 'F:/workspace'
    })
    useUIStore.setState({
      activeDetailTab: { kind: 'system', id: 'changes' },
      detailPanelPreferredVisible: false,
      detailPanelVisible: false
    })
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: { get: settingsGet },
        workspace: {
          viewer: {
            authorizeFile,
            classify
          }
        },
        shell: { openExternal: vi.fn() }
      }
    })
  })

  it('opens external pending file and image attachments in the internal viewer', async () => {
    const { container } = renderWithLocale(
      <AttachmentStrip
        images={[{
          tempPath: 'D:/pics/photo.png',
          dataUrl: 'data:image/png;base64,AA==',
          fileName: 'photo.png',
          mimeType: 'image/png'
        }]}
        files={[{ path: 'C:\\temp\\notes.txt', fileName: 'notes.txt' }]}
        onRemoveImage={() => {}}
        onRemoveFile={() => {}}
      />
    )

    const imageButton = container.querySelector('img')?.closest('button')
    expect(imageButton).not.toBeNull()
    fireEvent.click(imageButton!)
    fireEvent.click(screen.getByRole('button', { name: 'Open notes.txt in DotCraft viewer' }))

    await waitFor(() => {
      expect(authorizeFile).toHaveBeenCalledWith({ absolutePath: 'D:/pics/photo.png' })
      expect(authorizeFile).toHaveBeenCalledWith({ absolutePath: 'C:/temp/notes.txt' })
    })
    const tabs = useViewerTabStore.getState().getThreadState('thread-1').tabs
    expect(tabs).toEqual(expect.arrayContaining([
      expect.objectContaining({ absolutePath: 'D:/pics/photo.png', contentClass: 'image' }),
      expect.objectContaining({ absolutePath: 'C:/temp/notes.txt', contentClass: 'text' })
    ]))
  })
})

// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { openConversationLink } from '../utils/conversationDeepLink'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { useToastStore } from '../stores/toastStore'

const classifyMock = vi.fn()
const authorizeFileMock = vi.fn()
const shellOpenExternalMock = vi.fn()

function t(key: string): string {
  return key
}

describe('openConversationLink', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    classifyMock.mockResolvedValue({
      contentClass: 'text',
      mime: 'text/plain',
      sizeBytes: 12
    })
    authorizeFileMock.mockImplementation(async ({ absolutePath }: { absolutePath: string }) => ({ absolutePath }))
    shellOpenExternalMock.mockResolvedValue(undefined)
    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        workspace: {
          viewer: {
            authorizeFile: authorizeFileMock,
            classify: classifyMock
          }
        },
        shell: {
          openExternal: shellOpenExternalMock
        }
      }
    })
    useViewerTabStore.setState({
      byThread: new Map(),
      currentThreadId: 'thread-1',
      currentWorkspacePath: 'C:/repo'
    })
    useUIStore.setState({
      detailPanelPreferredVisible: false,
      detailPanelVisible: false,
      responsiveLayout: 'full',
      sidebarPreferredCollapsed: false,
      sidebarCollapsed: false,
      activeDetailTab: { kind: 'system', id: 'changes' }
    })
    useToastStore.setState({ toasts: [] })
  })

  it('opens file links in viewer and shows detail panel', async () => {
    const ok = await openConversationLink({
      target: './README.md',
      workspacePath: 'C:/repo',
      threadId: 'thread-1',
      t
    })
    expect(ok).toBe(true)
    expect(authorizeFileMock).toHaveBeenCalledWith({ absolutePath: 'C:/repo/README.md' })
    expect(classifyMock).toHaveBeenCalledWith({ absolutePath: 'C:/repo/README.md' })
    const tabs = useViewerTabStore.getState().getThreadState('thread-1').tabs
    expect(tabs).toHaveLength(1)
    expect(tabs[0]?.kind).toBe('file')
    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(true)
  })

  it('preserves line and column hints on newly opened file viewer tabs', async () => {
    const ok = await openConversationLink({
      target: './README.md:7:2',
      workspacePath: 'C:/repo',
      threadId: 'thread-1',
      t
    })
    expect(ok).toBe(true)
    expect(authorizeFileMock).toHaveBeenCalledWith({ absolutePath: 'C:/repo/README.md' })
    const tabs = useViewerTabStore.getState().getThreadState('thread-1').tabs
    expect(tabs).toHaveLength(1)
    expect(tabs[0]).toMatchObject({
      kind: 'file',
      absolutePath: 'C:/repo/README.md',
      navigationHint: { line: 7, column: 2 }
    })
  })

  it('focuses existing browser tab by normalized URL', async () => {
    const existingId = useViewerTabStore.getState().openBrowser({
      threadId: 'thread-1',
      initialUrl: 'https://example.com/'
    })
    const ok = await openConversationLink({
      target: 'https://example.com#fragment',
      workspacePath: 'C:/repo',
      threadId: 'thread-1',
      t
    })
    expect(ok).toBe(true)
    const state = useViewerTabStore.getState().getThreadState('thread-1')
    expect(state.tabs).toHaveLength(1)
    expect(state.activeTabId).toBe(existingId)
  })

  it('forces new tab on Ctrl/Cmd flow', async () => {
    await openConversationLink({
      target: './README.md',
      workspacePath: 'C:/repo',
      threadId: 'thread-1',
      t
    })
    await openConversationLink({
      target: './README.md',
      workspacePath: 'C:/repo',
      threadId: 'thread-1',
      forceNew: true,
      t
    })
    const tabs = useViewerTabStore.getState().getThreadState('thread-1').tabs
    expect(tabs).toHaveLength(2)
  })

  it('opens workspace-external file links after authorizing the exact file', async () => {
    authorizeFileMock.mockResolvedValue({ absolutePath: 'D:/reference/outside.png' })
    classifyMock.mockResolvedValue({
      contentClass: 'image',
      mime: 'image/png',
      sizeBytes: 20
    })

    const ok = await openConversationLink({
      target: 'D:/reference/outside.png',
      workspacePath: 'C:/repo',
      threadId: 'thread-1',
      t
    })

    expect(ok).toBe(true)
    expect(authorizeFileMock).toHaveBeenCalledWith({ absolutePath: 'D:/reference/outside.png' })
    const tab = useViewerTabStore.getState().getThreadState('thread-1').tabs[0]
    expect(tab).toMatchObject({
      kind: 'file',
      absolutePath: 'D:/reference/outside.png',
      relativePath: 'D:/reference/outside.png',
      contentClass: 'image'
    })
  })

  it('hands off external links without opening panel', async () => {
    useUIStore.getState().setDetailPanelVisible(false)
    const ok = await openConversationLink({
      target: 'mailto:test@example.com',
      workspacePath: 'C:/repo',
      threadId: 'thread-1',
      t
    })
    expect(ok).toBe(true)
    expect(shellOpenExternalMock).toHaveBeenCalledWith('mailto:test@example.com')
    expect(useUIStore.getState().detailPanelPreferredVisible).toBe(false)
  })

  it('toasts on unsupported links', async () => {
    const ok = await openConversationLink({
      target: 'javascript:alert(1)',
      workspacePath: 'C:/repo',
      threadId: 'thread-1',
      t
    })
    expect(ok).toBe(false)
    const toasts = useToastStore.getState().toasts
    expect(toasts).toHaveLength(1)
    expect(toasts[0]?.message).toBe('conversation.deepLink.rejectUnsupported')
  })
})

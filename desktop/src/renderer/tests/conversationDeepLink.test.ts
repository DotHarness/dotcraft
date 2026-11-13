// @vitest-environment jsdom
import { getRendererTestState, setRendererTestState } from './panelTestState'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { openConversationLink } from '../utils/conversationDeepLink'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { useToastStore } from '../stores/toastStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useThreadStore } from '../stores/threadStore'
import { installDesktopApiMock } from './desktopApiMock'

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
    installDesktopApiMock({
      workspace: {
        viewer: {
          authorizeFile: authorizeFileMock,
          classify: classifyMock
        },
      },
      shell: {
        openExternal: shellOpenExternalMock
      }
    })
    useDetailPanelStore.setState({
      byThread: new Map(),
      currentThreadId: 'thread-1',
      currentWorkspacePath: 'C:/repo'
    })
    setRendererTestState({
      detailPanelPreferredVisible: false,
      detailPanelVisible: false,
      responsiveLayout: 'full',
      sidebarPreferredCollapsed: false,
      sidebarCollapsed: false,
      activeDetailTab: { kind: 'system', id: 'changes' }
    })
    useToastStore.setState({ toasts: [] })
    useThreadStore.getState().reset()
  })

  it('opens file links with line and column hints in the viewer and shows the detail panel', async () => {
    const ok = await openConversationLink({
      target: './README.md:7:2',
      workspacePath: 'C:/repo',
      threadId: 'thread-1',
      t
    })
    expect(ok).toBe(true)
    expect(authorizeFileMock).toHaveBeenCalledWith({ absolutePath: 'C:/repo/README.md', workspacePath: 'C:/repo' })
    expect(useDetailPanelStore.getState().getThreadState('thread-1').tabs[0]).toMatchObject({
      kind: 'file',
      absolutePath: 'C:/repo/README.md',
      navigationHint: { line: 7, column: 2 }
    })
    expect(classifyMock).toHaveBeenCalledWith({ absolutePath: 'C:/repo/README.md' })
    expect(useDetailPanelStore.getState().getThreadState('thread-1').tabs).toHaveLength(1)
    expect(getRendererTestState().detailPanelPreferredVisible).toBe(true)
  })

  it('focuses existing browser tab by normalized URL', async () => {
    const existingId = useDetailPanelStore.getState().openBrowser({
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
    const state = useDetailPanelStore.getState().getThreadState('thread-1')
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
    const tabs = useDetailPanelStore.getState().getThreadState('thread-1').tabs
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
    expect(authorizeFileMock).toHaveBeenCalledWith({ absolutePath: 'D:/reference/outside.png', workspacePath: 'C:/repo' })
    const tab = useDetailPanelStore.getState().getThreadState('thread-1').tabs[0]
    expect(tab).toMatchObject({
      kind: 'file',
      absolutePath: 'D:/reference/outside.png',
      relativePath: 'D:/reference/outside.png',
      contentClass: 'image'
    })
  })

  it('uses the owning Project for attached files while the thread works in a worktree', async () => {
    useThreadStore.setState({ threadList: [{
      id: 'thread-1', workspacePath: 'C:/repo', effectiveWorkspacePath: 'C:/worktree',
      displayName: null, status: 'active', originChannel: 'dotcraft-desktop',
      createdAt: '2026-11-06T00:00:00Z', lastActiveAt: '2026-11-06T00:00:00Z'
    }] })
    useWorkspaceProjectsStore.setState({
      projects: [{
        kind: 'local',
        path: 'C:/repo',
        name: 'repo',
        state: 'foreground',
        running: true,
        loaded: true,
        threadCount: 0,
        threads: [],
        pinned: false,
        secondaryFolders: ['D:/design']
      }]
    })
    try {
      const ok = await openConversationLink({
        target: 'D:/design/tokens/colors.md',
        workspacePath: 'C:/worktree',
        threadId: 'thread-1',
        t
      })
      expect(ok).toBe(true)
      expect(authorizeFileMock).toHaveBeenCalledWith({
        absolutePath: 'D:/design/tokens/colors.md', workspacePath: 'C:/repo'
      })
      expect(useDetailPanelStore.getState().getThreadState('thread-1').tabs[0]).toMatchObject({
        absolutePath: 'D:/design/tokens/colors.md',
        relativePath: 'tokens/colors.md'
      })
    } finally {
      useWorkspaceProjectsStore.setState({ projects: [] })
    }
  })

  it.each(['thread', 'project'])('does not publish a stale file open after switching %s during preparation', async selection => {
    let complete!: (value: { contentClass: string; sizeBytes: number }) => void
    const classification = new Promise<{ contentClass: string; sizeBytes: number }>(resolve => { complete = resolve })
    let started!: () => void
    const preparing = new Promise<void>(resolve => { started = resolve })
    classifyMock.mockImplementationOnce(() => {
      started()
      return classification
    })
    const pending = openConversationLink({
      target: './README.md', workspacePath: 'C:/repo', threadId: 'thread-1', t
    })
    await preparing
    const store = useDetailPanelStore.getState()
    if (selection === 'project') store.onWorkspaceSwitched('D:/other')
    store.onThreadSwitched('thread-2')
    const incoming = useDetailPanelStore.getState().getCurrentPanel()
    complete({ contentClass: 'text', sizeBytes: 12 })
    await pending
    expect(useDetailPanelStore.getState().getCurrentPanel()).toBe(incoming)
    expect(useDetailPanelStore.getState().getThreadState('thread-1').tabs).toEqual([])
  })

  it('hands off external links without opening panel and toasts on unsupported links', async () => {
    getRendererTestState().setDetailPanelVisible(false)
    const ok = await openConversationLink({
      target: 'mailto:test@example.com',
      workspacePath: 'C:/repo',
      threadId: 'thread-1',
      t
    })
    expect(ok).toBe(true)
    expect(shellOpenExternalMock).toHaveBeenCalledWith('mailto:test@example.com')
    expect(getRendererTestState().detailPanelPreferredVisible).toBe(false)

    const rejected = await openConversationLink({
      target: 'javascript:alert(1)',
      workspacePath: 'C:/repo',
      threadId: 'thread-1',
      t
    })
    expect(rejected).toBe(false)
    const toasts = useToastStore.getState().toasts
    expect(toasts).toHaveLength(1)
    expect(toasts[0]?.message).toBe('conversation.deepLink.rejectUnsupported')
  })
})

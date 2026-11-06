// @vitest-environment jsdom
import { getRendererTestState, setRendererTestState } from './panelTestState'
// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DiffAnnotationContext } from '../../shared/composerContext'
import { useComposerContextStore } from '../stores/composerContextStore'
import { useLineCommentDraftStore } from '../stores/lineCommentDraftStore'
import { useThreadStore } from '../stores/threadStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { handOffWelcomePanel } from '../utils/welcomePanelHandoff'
import { installDesktopApiMock } from './desktopApiMock'

const WELCOME = 'welcome:/workspace/path'
const THREAD = 'thread-new'

function lineComment(id: string): DiffAnnotationContext {
  return {
    kind: 'diffAnnotation',
    id,
    path: '/workspace/path/src/app.ts',
    side: 'right',
    startLine: 3,
    endLine: 3,
    comment: 'why?'
  }
}

describe('handOffWelcomePanel', () => {
  const rebindThread = vi.fn(async () => undefined)
  const rebindTerminalThread = vi.fn(async () => undefined)

  beforeEach(() => {
    rebindThread.mockClear()
    rebindTerminalThread.mockClear()
    installDesktopApiMock({ workspace: { viewer: { browser: { rebindThread }, terminal: { rebindThread: rebindTerminalThread } } } })
    useThreadStore.setState({ activeThreadId: null })
    useDetailPanelStore.setState({
      byThread: new Map(),
      currentThreadId: WELCOME,
      welcomeScopeId: WELCOME,
      currentWorkspacePath: '/workspace/path'
    })
    setRendererTestState({
      detailPanelPreferredVisible: false,
      detailPanelVisible: false,
      responsiveLayout: 'full',
      openSystemTabs: [],
      activeDetailTab: { kind: 'launcher' }
    })
    useLineCommentDraftStore.setState({ byThread: {} })
    useComposerContextStore.setState({ byThread: {} })
  })

  it('moves the open Welcome panel, its sessions and unsent comments to the new thread', () => {
    const viewer = useDetailPanelStore.getState()
    const browserTabId = viewer.openBrowser({ threadId: WELCOME })
    const terminalTabId = viewer.openTerminal({ threadId: WELCOME, cwd: '/workspace/path' })
    useDetailPanelStore.getState().setActiveViewerTab(terminalTabId)
    useLineCommentDraftStore.getState().open(WELCOME, lineComment('draft-1'))
    useComposerContextStore.getState().addContext(WELCOME, lineComment('saved-1'))

    handOffWelcomePanel(WELCOME, THREAD)

    const viewerState = useDetailPanelStore.getState()
    expect(viewerState.currentThreadId).toBe(THREAD)
    expect(viewerState.getThreadState(THREAD).tabs.map((tab) => tab.id)).toEqual([browserTabId, terminalTabId])
    expect(viewerState.getThreadState(THREAD).activeTabId).toBe(terminalTabId)
    expect(rebindThread).toHaveBeenCalledWith({ fromThreadId: WELCOME, toThreadId: THREAD })
    expect(rebindTerminalThread).toHaveBeenCalledWith({ fromThreadId: WELCOME, toThreadId: THREAD })
    expect(useLineCommentDraftStore.getState().getDrafts(THREAD).map((draft) => draft.id)).toEqual(['draft-1'])
    expect(useComposerContextStore.getState().getContexts(THREAD).map((context) => context.id)).toEqual(['saved-1'])

    useThreadStore.setState({ activeThreadId: THREAD })
    useDetailPanelStore.getState().onThreadSwitched(THREAD)
    expect(getRendererTestState().detailPanelVisible).toBe(true)
    expect(getRendererTestState().activeDetailTab).toEqual({ kind: 'viewer', id: terminalTabId })
  })

  it('leaves the next Welcome visit with a closed, empty panel', () => {
    const tabId = useDetailPanelStore.getState().openTerminal({ threadId: WELCOME, cwd: '/workspace/path' })
    useDetailPanelStore.getState().setActiveViewerTab(tabId)
    useLineCommentDraftStore.getState().open(WELCOME, lineComment('draft-1'))

    handOffWelcomePanel(WELCOME, THREAD)
    useDetailPanelStore.getState().onThreadSwitched(WELCOME)

    expect(useDetailPanelStore.getState().getThreadState(WELCOME).tabs).toEqual([])
    expect(useLineCommentDraftStore.getState().getDrafts(WELCOME)).toEqual([])
    expect(useComposerContextStore.getState().getContexts(WELCOME)).toEqual([])
    expect(getRendererTestState().detailPanelVisible).toBe(false)
    expect(getRendererTestState().activeDetailTab).toEqual({ kind: 'launcher' })
  })
})

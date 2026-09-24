// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { DiffAnnotationContext } from '../../shared/composerContext'
import { useComposerContextStore } from '../stores/composerContextStore'
import { useLineCommentDraftStore } from '../stores/lineCommentDraftStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
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
    selectedText: 'const app = 1',
    comment: 'why?'
  }
}

describe('handOffWelcomePanel', () => {
  const rebindThread = vi.fn(async () => undefined)

  beforeEach(() => {
    rebindThread.mockClear()
    installDesktopApiMock({ workspace: { viewer: { browser: { rebindThread } } } })
    useThreadStore.setState({ activeThreadId: null })
    useViewerTabStore.setState({
      byThread: new Map(),
      currentThreadId: WELCOME,
      welcomeScopeId: WELCOME,
      currentWorkspacePath: '/workspace/path'
    })
    useUIStore.setState({
      detailPanelPreferredVisibleByThread: {},
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
    const viewer = useViewerTabStore.getState()
    const browserTabId = viewer.openBrowser({ threadId: WELCOME })
    const terminalTabId = viewer.openTerminal({ threadId: WELCOME, cwd: '/workspace/path' })
    useUIStore.getState().setActiveViewerTab(terminalTabId)
    useLineCommentDraftStore.getState().open(WELCOME, lineComment('draft-1'))
    useComposerContextStore.getState().addContext(WELCOME, lineComment('saved-1'))

    handOffWelcomePanel(WELCOME, THREAD)

    const viewerState = useViewerTabStore.getState()
    expect(viewerState.currentThreadId).toBe(THREAD)
    expect(viewerState.getThreadState(THREAD).tabs.map((tab) => tab.id)).toEqual([browserTabId, terminalTabId])
    expect(viewerState.getThreadState(THREAD).activeTabId).toBe(terminalTabId)
    expect(rebindThread).toHaveBeenCalledWith({ fromThreadId: WELCOME, toThreadId: THREAD })
    expect(useLineCommentDraftStore.getState().getDrafts(THREAD).map((draft) => draft.id)).toEqual(['draft-1'])
    expect(useComposerContextStore.getState().getContexts(THREAD).map((context) => context.id)).toEqual(['saved-1'])

    useThreadStore.setState({ activeThreadId: THREAD })
    useUIStore.getState().syncDetailPanelForThread(THREAD, terminalTabId)
    expect(useUIStore.getState().detailPanelVisible).toBe(true)
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'viewer', id: terminalTabId })
  })

  it('leaves the next Welcome visit with a closed, empty panel', () => {
    const tabId = useViewerTabStore.getState().openTerminal({ threadId: WELCOME, cwd: '/workspace/path' })
    useUIStore.getState().setActiveViewerTab(tabId)
    useLineCommentDraftStore.getState().open(WELCOME, lineComment('draft-1'))

    handOffWelcomePanel(WELCOME, THREAD)
    useUIStore.getState().syncDetailPanelForThread(WELCOME, null)

    expect(useViewerTabStore.getState().getThreadState(WELCOME).tabs).toEqual([])
    expect(useLineCommentDraftStore.getState().getDrafts(WELCOME)).toEqual([])
    expect(useComposerContextStore.getState().getContexts(WELCOME)).toEqual([])
    expect(useUIStore.getState().detailPanelVisible).toBe(false)
    expect(useUIStore.getState().activeDetailTab).toEqual({ kind: 'launcher' })
  })
})

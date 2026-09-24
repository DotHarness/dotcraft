import { useComposerContextStore } from '../stores/composerContextStore'
import { useLineCommentDraftStore } from '../stores/lineCommentDraftStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'

function moveEntry<T>(record: Record<string, T>, from: string, to: string): Record<string, T> {
  if (!(from in record)) return record
  const { [from]: value, ...rest } = record
  return { ...rest, [to]: value }
}

export function handOffWelcomePanel(welcomeScopeId: string, threadId: string): void {
  const welcome = useViewerTabStore.getState().byThread.get(welcomeScopeId)
  useViewerTabStore.setState((state) => {
    const byThread = new Map(state.byThread)
    byThread.delete(welcomeScopeId)
    if (welcome) byThread.set(threadId, welcome)
    return {
      byThread,
      currentThreadId: state.currentThreadId === welcomeScopeId ? threadId : state.currentThreadId
    }
  })
  if (welcome?.tabs.some((tab) => tab.kind === 'browser')) {
    void window.api.workspace.viewer.browser.rebindThread({ fromThreadId: welcomeScopeId, toThreadId: threadId })
  }

  useUIStore.setState((state) => ({
    detailPanelPreferredVisibleByThread: moveEntry(state.detailPanelPreferredVisibleByThread, welcomeScopeId, threadId)
  }))
  useLineCommentDraftStore.setState((state) => ({
    byThread: moveEntry(state.byThread, welcomeScopeId, threadId)
  }))

  const contexts = useComposerContextStore.getState()
  const leftover = contexts.getContexts(welcomeScopeId)
  if (leftover.length > 0) {
    contexts.setContexts(threadId, [...contexts.getContexts(threadId), ...leftover])
    contexts.clearContexts(welcomeScopeId)
  }
}

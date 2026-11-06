import { useComposerContextStore } from '../stores/composerContextStore'
import { useLineCommentDraftStore } from '../stores/lineCommentDraftStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'

function moveEntry<T>(record: Record<string, T>, from: string, to: string): Record<string, T> {
  if (!(from in record)) return record
  const { [from]: value, ...rest } = record
  return { ...rest, [to]: value }
}

export function handOffWelcomePanel(welcomeScopeId: string, threadId: string): void {
  const welcome = useDetailPanelStore.getState().handOffWelcomePanel(welcomeScopeId, threadId)
  if (welcome?.tabs.some((tab) => tab.kind === 'browser')) {
    void window.api.workspace.viewer.browser.rebindThread({ fromThreadId: welcomeScopeId, toThreadId: threadId })
  }
  if (welcome?.tabs.some((tab) => tab.kind === 'terminal')) {
    void window.api.workspace.viewer.terminal.rebindThread({ fromThreadId: welcomeScopeId, toThreadId: threadId })
  }

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

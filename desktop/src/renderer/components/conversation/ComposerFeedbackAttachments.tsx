import type { ComposerContextRecord } from '../../../shared/composerContext'
import { useComposerContextStore } from '../../stores/composerContextStore'
import { ContextFeedback } from './ContextFeedback'

export function ComposerFeedbackAttachments({ threadId, contexts }: {
  threadId: string; contexts: Exclude<ComposerContextRecord, { kind: 'pastedText' }>[]
}): JSX.Element | null {
  return <ContextFeedback contexts={contexts}
    onRemove={id => useComposerContextStore.getState().removeContext(threadId, id)}
    onEdit={(id, comment) => {
      const store = useComposerContextStore.getState()
      store.setContexts(threadId, store.getContexts(threadId).map(context => context.id === id ? { ...context, comment } : context))
    }} />
}

import type { InputPart, QueuedTurnInput } from '../types/conversation'
import { useComposerContextStore } from '../stores/composerContextStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { queuedInputToComposerDraft } from './composerHistory'
import { mergeRestoredComposerDraft } from './composerSubmission'
import type { ComposerDraftSegment } from '../types/composerDraft'

export async function restoreRejectedWelcomeInput(threadId: string, input: InputPart[]): Promise<void> {
  const submitted = await queuedInputToComposerDraft({ nativeInputParts: input } as QueuedTurnInput)
  const stored = useComposerDraftStore.getState().getDraft(threadId)
  const contextStore = useComposerContextStore.getState()
  const currentSegments: ComposerDraftSegment[] = stored?.segments.length
    ? stored.segments
    : stored?.text ? [{ type: 'text', value: stored.text }] : []
  const draft = mergeRestoredComposerDraft(submitted, {
    text: stored?.text ?? '',
    segments: currentSegments,
    contexts: contextStore.getContexts(threadId),
    files: stored?.files ?? [],
    images: stored?.images ?? []
  })
  useComposerDraftStore.getState().saveDraft(threadId, draft)
  contextStore.setContexts(threadId, draft.contexts ?? [])
  contextStore.requestRestore(threadId, draft)
}

import type { InputPart, QueuedTurnInput } from '../types/conversation'
import { useComposerContextStore } from '../stores/composerContextStore'
import { useComposerDraftStore } from '../stores/composerDraftStore'
import { projectInputParts } from './inputPresentation'
import { queuedInputToComposerDraft } from './composerHistory'
import { stringifyComposerDraftSegments } from '../components/conversation/richInputSerialization'
import type { ComposerDraftSegment } from '../types/composerDraft'

export function acceptWelcomeInput(threadId: string, input: InputPart[]): void {
  const accepted = new Set(projectInputParts(input).contexts.map((context) => context.id))
  const store = useComposerContextStore.getState()
  store.setContexts(threadId, store.getContexts(threadId).filter((context) => !accepted.has(context.id)))
}

export async function restoreRejectedWelcomeInput(threadId: string, input: InputPart[]): Promise<void> {
  const restored = await queuedInputToComposerDraft({ nativeInputParts: input } as QueuedTurnInput)
  const current = useComposerDraftStore.getState().getDraft(threadId)
  const contextStore = useComposerContextStore.getState()
  const contexts = [...new Map([...(restored.contexts ?? []), ...contextStore.getContexts(threadId)].map((context) => [context.id, context])).values()]
  const currentSegments: ComposerDraftSegment[] = current?.segments.length ? current.segments : current?.text ? [{ type: 'text', value: current.text }] : []
  const segments: ComposerDraftSegment[] = JSON.stringify(currentSegments) === JSON.stringify(restored.segments) || currentSegments.length === 0
    ? restored.segments
    : [...restored.segments, ...(restored.segments.length ? [{ type: 'text' as const, value: '\n\n' }] : []), ...currentSegments]
  const files = [...new Map([...restored.files, ...(current?.files ?? [])].map((file) => [file.path, file])).values()]
  const images = [...new Map([...restored.images, ...(current?.images ?? [])].map((image) => [image.tempPath, image])).values()]
  const draft = { ...restored, text: stringifyComposerDraftSegments(segments), segments, files, images, contexts }
  useComposerDraftStore.getState().saveDraft(threadId, draft)
  contextStore.setContexts(threadId, contexts)
  contextStore.requestRestore(threadId, draft)
}

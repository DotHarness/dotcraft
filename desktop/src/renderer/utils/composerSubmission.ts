import { stringifyComposerDraftSegments } from '../components/conversation/richInputSerialization'
import type { ComposerDraftSegment } from '../types/composerDraft'
import type { ComposerDraftSnapshot } from './composerHistory'
import { mergeComposerFileAttachments } from './composerAttachments'

export function emptyComposerDraftSnapshot(): ComposerDraftSnapshot {
  return { text: '', segments: [], contexts: [], files: [], images: [] }
}

/** Returns a failed submission above whatever the composer gained while it was in flight. */
export function mergeRestoredComposerDraft(
  submitted: ComposerDraftSnapshot,
  current: ComposerDraftSnapshot
): ComposerDraftSnapshot {
  const submittedText = stringifyComposerDraftSegments(submitted.segments)
  const currentText = stringifyComposerDraftSegments(current.segments)
  const keepsCurrentText = currentText.trim().length > 0 && currentText !== submittedText
  const segments: ComposerDraftSegment[] = keepsCurrentText
    ? [...submitted.segments, { type: 'text', value: '\n\n' }, ...current.segments]
    : submitted.segments
  const submittedContexts = submitted.contexts ?? []
  return {
    clientUserMessageId: submitted.clientUserMessageId,
    text: stringifyComposerDraftSegments(segments),
    segments,
    contexts: [
      ...submittedContexts,
      ...(current.contexts ?? []).filter((context) => !submittedContexts.some((kept) => kept.id === context.id))
    ],
    files: mergeComposerFileAttachments(submitted.files, current.files),
    images: [
      ...submitted.images,
      ...current.images.filter((image) => !submitted.images.some((kept) => kept.tempPath === image.tempPath))
    ]
  }
}

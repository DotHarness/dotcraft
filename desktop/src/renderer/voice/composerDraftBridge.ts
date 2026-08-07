import { useComposerDraftStore, type ThreadComposerDraftInput } from '../stores/composerDraftStore'
import type { ComposerDraftSegment } from '../types/composerDraft'

export interface ComposerVoiceTarget {
  capture(): ThreadComposerDraftInput
  apply(draft: ThreadComposerDraftInput): void
  submit(): Promise<void>
}

const mountedTargets = new Map<string, ComposerVoiceTarget>()

export function registerComposerVoiceTarget(threadId: string, target: ComposerVoiceTarget): () => void {
  if (!threadId) return () => {}
  mountedTargets.set(threadId, target)
  return () => {
    if (mountedTargets.get(threadId) === target) mountedTargets.delete(threadId)
  }
}

export function isAvailableComposerVoiceOrigin(threadId: string): boolean {
  return mountedTargets.has(threadId)
}

export async function appendVoiceTranscript(
  threadId: string,
  transcript: string,
  send: boolean
): Promise<boolean> {
  const trimmed = transcript.trim()
  if (!trimmed) return false
  const target = mountedTargets.get(threadId)
  const stored = useComposerDraftStore.getState().getDraft(threadId)
  const current = target?.capture() ?? (stored ? {
    text: stored.text,
    segments: [...stored.segments],
    images: [...stored.images],
    files: [...stored.files]
  } : emptyDraft())
  const next = appendTranscriptToDraft(current, trimmed)
  useComposerDraftStore.getState().saveDraft(threadId, next)
  target?.apply(next)
  if (send && target) await target.submit()
  return true
}

export function appendTranscriptToDraft(
  draft: ThreadComposerDraftInput,
  transcript: string
): ThreadComposerDraftInput {
  const trimmed = transcript.trim()
  if (!trimmed) return cloneDraft(draft)
  const separator = draft.text.length > 0 && !/\s$/.test(draft.text) ? ' ' : ''
  const suffix = `${separator}${trimmed}`
  const segments = appendTextSegment(draft.segments, suffix, draft.text)
  return {
    text: `${draft.text}${suffix}`,
    segments,
    images: [...draft.images],
    files: [...draft.files]
  }
}

function appendTextSegment(
  source: ComposerDraftSegment[],
  suffix: string,
  fallbackText: string
): ComposerDraftSegment[] {
  const segments = source.length > 0
    ? source.map((segment) => ({ ...segment }))
    : fallbackText
      ? [{ type: 'text' as const, value: fallbackText }]
      : []
  const last = segments.at(-1)
  if (last?.type === 'text') last.value += suffix
  else segments.push({ type: 'text', value: suffix })
  return segments
}

function cloneDraft(draft: ThreadComposerDraftInput): ThreadComposerDraftInput {
  return {
    text: draft.text,
    segments: draft.segments.map((segment) => ({ ...segment })),
    images: [...draft.images],
    files: [...draft.files]
  }
}

function emptyDraft(): ThreadComposerDraftInput {
  return { text: '', segments: [], images: [], files: [] }
}

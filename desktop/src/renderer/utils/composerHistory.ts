import type { ComposerContextRecord } from '../../shared/composerContext'
import type { ComposerDraftSegment } from '../types/composerDraft'
import type { ComposerFileAttachment, ImageAttachment, InputPart, ConversationItem, ConversationTurn, QueuedTurnInput } from '../types/conversation'
import { stringifyComposerDraftSegments } from '../components/conversation/richInputSerialization'
import { projectInputParts } from './inputPresentation'
import { splitThreadMentions } from './threadReferences'
export interface ComposerHistoryEntry {
  clientUserMessageId?: string
  text: string
  segments: ComposerDraftSegment[]
  contexts?: ComposerContextRecord[]
  images?: ImageAttachment[]
}
export interface ComposerDraftSnapshot extends ComposerHistoryEntry {
  files: ComposerFileAttachment[]
  images: ImageAttachment[]
}
export function buildComposerHistory(turns: ConversationTurn[], threadId: string): ComposerHistoryEntry[] {
  const entries: ComposerHistoryEntry[] = []
  for (const turn of turns) {
    if (turn.threadId !== threadId) continue
    for (const item of turn.items) {
      const entry = userItemToComposerHistoryEntry(item)
      if (entry) entries.push(entry)
    }
  }
  return entries
}

function userItemToComposerHistoryEntry(item: ConversationItem): ComposerHistoryEntry | null {
  if (item.type !== 'userMessage') return null
  if (item.deliveryMode === 'guidance') return null
  const text = item.text ?? ''

  const inputParts = item.nativeInputParts
  if (inputParts && inputParts.length > 0) {
    const projected = projectInputParts(inputParts)
    const segments = inputPartsToComposerSegments(projected.parts)
    const serialized = stringifyComposerDraftSegments(segments).trim()
    if (serialized.length === 0 && projected.contexts.length === 0 && projected.images.length === 0 && projected.imageDataUrls.length === 0) return null
    return {
      text: serialized,
      contexts: projected.contexts,
      images: projected.images.map((image) => ({ tempPath: image.path, dataUrl: '', mimeType: image.mimeType ?? 'image/png', fileName: image.fileName ?? fileNameFromPath(image.path) })),
      segments
    }
  }

  if (!text.trim()) return null
  return { text, segments: [], contexts: [] }
}

function inputPartsToComposerSegments(parts: InputPart[]): ComposerDraftSegment[] {
  const segments: ComposerDraftSegment[] = []
  for (const part of parts) {
    switch (part.type) {
      case 'text':
        for (const piece of splitThreadMentions(part.text)) {
          if (piece.type === 'thread') segments.push(piece)
          else pushComposerTextSegment(segments, piece.value)
        }
        break
      case 'fileRef':
        segments.push({ type: 'file', relativePath: part.displayPath ?? part.path })
        break
      case 'commandRef':
        pushCommandRefSegments(segments, part)
        break
      case 'skillRef':
        if (part.name.trim().length > 0) {
          segments.push({ type: 'skill', skillName: part.name.trim() })
        }
        break
      default:
        break
    }
  }
  return segments
}

export async function queuedInputToComposerDraft(item: QueuedTurnInput): Promise<ComposerDraftSnapshot> {
  const parts = item.nativeInputParts?.length
    ? item.nativeInputParts
    : null

  if (!parts) {
    if (!item.displayText) throw new Error('Queued message has no editable content.')
    return {
      clientUserMessageId: item.clientUserMessageId,
      text: item.displayText,
      contexts: [],
      segments: [{ type: 'text', value: item.displayText }],
      files: [],
      images: []
    }
  }

  if (parts.some((part) => part.type === 'image')) {
    throw new Error('Remote image inputs cannot be restored in the composer.')
  }

  const projected = projectInputParts(parts)
  const segments = inputPartsToComposerSegments(projected.parts)
  const images: ImageAttachment[] = []
  for (const context of projected.contexts) {
    if (context.kind !== 'pageReference' || !context.image) continue
    const { dataUrl } = await window.api.workspace.readImageAsDataUrl({ path: context.image.tempPath }).catch(() => ({ dataUrl: '' }))
    context.image = { ...context.image, dataUrl }
  }
  for (const part of parts) {
    if (part.type !== 'localImage') continue
    const { dataUrl } = await window.api.workspace.readImageAsDataUrl({ path: part.path })
    if (!dataUrl) throw new Error(`Unable to read queued image: ${part.fileName || part.path}`)
    images.push({ tempPath: part.path, dataUrl, fileName: part.fileName || fileNameFromPath(part.path), mimeType: part.mimeType || mimeTypeFromDataUrl(dataUrl) || 'image/png' })
  }

  return {
    clientUserMessageId: item.clientUserMessageId,
    contexts: projected.contexts,
    text: stringifyComposerDraftSegments(segments),
    segments,
    files: [],
    images
  }
}

function fileNameFromPath(path: string): string {
  return path.split(/[/\\]/).pop() || path
}

function mimeTypeFromDataUrl(dataUrl: string): string | null {
  const match = /^data:([^;,]+)[;,]/i.exec(dataUrl)
  return match?.[1] ?? null
}

function pushComposerTextSegment(segments: ComposerDraftSegment[], value: string): void {
  if (value.length === 0) return
  const previous = segments[segments.length - 1]
  if (previous?.type === 'text') {
    previous.value += value
    return
  }
  segments.push({ type: 'text', value })
}

function pushCommandRefSegments(
  segments: ComposerDraftSegment[],
  part: Extract<InputPart, { type: 'commandRef' }>
): void {
  const rawText = typeof part.rawText === 'string' ? part.rawText.trim() : ''
  const name = typeof part.name === 'string' ? part.name.trim().replace(/^\/+/, '') : ''
  const normalizedRaw = rawText.length > 0
    ? (rawText.startsWith('/') ? rawText : `/${rawText}`)
    : name.length > 0
      ? `/${name}`
      : ''
  if (normalizedRaw.length === 0) return

  const firstWhitespace = normalizedRaw.search(/\s/)
  const command = firstWhitespace >= 0 ? normalizedRaw.slice(0, firstWhitespace) : normalizedRaw
  const rawArgs = firstWhitespace >= 0 ? normalizedRaw.slice(firstWhitespace + 1).trim() : ''
  const argsText = (part.argsText?.trim() || rawArgs).trim()
  segments.push({ type: 'command', command })
  if (argsText.length > 0) {
    pushComposerTextSegment(segments, ` ${argsText}`)
  }
}

export function linearLengthOfComposerEntry(entry: ComposerHistoryEntry): number {
  if (entry.segments.length === 0) return entry.text.length
  return entry.segments.reduce((total, segment) => {
    if (segment.type === 'text') return total + segment.value.length
    return total + 1
  }, 0)
}

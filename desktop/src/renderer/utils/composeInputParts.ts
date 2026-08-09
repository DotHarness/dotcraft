import type { ComposerDraftSegment } from '../types/composerDraft'
import type { ComposerFileAttachment, ImageAttachment, InputPart } from '../types/conversation'
import { stringifyComposerDraftSegments } from '../components/conversation/richInputSerialization'

interface BuildComposerInputPartsArgs {
  text: string
  segments?: ComposerDraftSegment[]
  files?: ComposerFileAttachment[]
  images?: ImageAttachment[]
}

interface BuildComposerInputPartsResult {
  inputParts: InputPart[]
  visibleText: string
  bodyText: string
}

function normalizeSegments(text: string, segments?: ComposerDraftSegment[]): ComposerDraftSegment[] {
  if (Array.isArray(segments) && segments.length > 0) return segments
  if (text.length === 0) return []
  return [{ type: 'text', value: text }]
}

function segmentsToInputParts(segments: ComposerDraftSegment[]): InputPart[] {
  return segments.flatMap<InputPart>((segment): InputPart[] => {
    switch (segment.type) {
      case 'text':
        return segment.value.length > 0 ? [{ type: 'text', text: segment.value } satisfies InputPart] : []
      case 'file':
        return [{ type: 'fileRef', path: segment.relativePath, displayPath: segment.relativePath } satisfies InputPart]
      case 'command': {
        const rawText = segment.command.trim()
        const normalized = rawText.startsWith('/') ? rawText : `/${rawText}`
        const firstWhitespace = normalized.search(/\s/)
        const commandToken = firstWhitespace >= 0 ? normalized.slice(0, firstWhitespace) : normalized
        const name = commandToken.slice(1)
        const argsText = firstWhitespace >= 0 ? normalized.slice(firstWhitespace + 1).trim() : ''
        return name.length > 0
          ? [{
            type: 'commandRef',
            name,
            rawText: normalized,
            ...(argsText ? { argsText } : {})
          } satisfies InputPart]
          : []
      }
      case 'skill':
        return segment.skillName.trim().length > 0
          ? [{ type: 'skillRef', name: segment.skillName.trim() } satisfies InputPart]
          : []
      default:
        return []
    }
  })
}

export function buildComposerInputParts({
  text,
  segments,
  files = [],
  images = []
}: BuildComposerInputPartsArgs): BuildComposerInputPartsResult {
  const normalizedSegments = normalizeSegments(text, segments)
  const bodyText = stringifyComposerDraftSegments(normalizedSegments).trim()
  const inputParts: InputPart[] = []
  const normalizedFiles = files
    .map((file) => file.path.trim())
    .filter((path) => path.length > 0)

  normalizedFiles.forEach((path, index) => {
    inputParts.push({
      type: 'fileRef',
      path,
      displayPath: path
    })
    if (index < normalizedFiles.length - 1) {
      inputParts.push({ type: 'text', text: '\n' })
    }
  })

  if (normalizedFiles.length > 0 && normalizedSegments.length > 0) {
    inputParts.push({ type: 'text', text: '\n\n' })
  }

  inputParts.push(...segmentsToInputParts(normalizedSegments))

  for (const image of images) {
    inputParts.push({
      type: 'localImage',
      path: image.tempPath,
      mimeType: image.mimeType,
      fileName: image.fileName
    })
  }

  return {
    inputParts,
    bodyText,
    visibleText: buildVisibleText(files, bodyText)
  }
}

function buildVisibleText(files: ComposerFileAttachment[], bodyText: string): string {
  const fileRefs = files
    .map((file) => file.path.trim())
    .filter((path) => path.length > 0)
    .map((path) => `@${path}`)

  if (fileRefs.length === 0) return bodyText
  const fileBlock = fileRefs.join('\n')
  return bodyText.length > 0 ? `${fileBlock}\n\n${bodyText}` : fileBlock
}

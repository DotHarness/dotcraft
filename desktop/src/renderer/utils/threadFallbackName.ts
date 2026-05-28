import { stripSystemReminderBlocks } from './systemReminderText'

interface ThreadFallbackNameArgs {
  visibleText: string
  imagesCount: number
  filesCount: number
  fallbackThreadName: string
  imageFallbackThreadName?: string
  fileFallbackThreadName?: string
  attachmentFallbackThreadName?: string
}

export function getFallbackThreadName({
  visibleText,
  imagesCount,
  filesCount,
  fallbackThreadName,
  imageFallbackThreadName,
  fileFallbackThreadName,
  attachmentFallbackThreadName
}: ThreadFallbackNameArgs): string {
  const trimmed = stripLeadingFileRefs(stripSystemReminderBlocks(visibleText), filesCount)
    .replace(/^\[\[Attached File: .+?\]\]\s*/gm, '')
    .trim()
  if (trimmed.length > 0) {
    return trimmed.length > 50 ? `${trimmed.slice(0, 50)}...` : trimmed
  }

  const hasImages = imagesCount > 0
  const hasFiles = filesCount > 0

  if (hasImages && hasFiles) {
    return attachmentFallbackThreadName ?? fallbackThreadName
  }
  if (hasFiles) {
    return fileFallbackThreadName ?? fallbackThreadName
  }
  if (hasImages) {
    return imageFallbackThreadName ?? fallbackThreadName
  }

  return fallbackThreadName
}

function stripLeadingFileRefs(text: string, filesCount: number): string {
  if (filesCount <= 0) return text

  const lines = text.split(/\r?\n/)
  let cursor = 0
  let stripped = 0

  while (cursor < lines.length && stripped < filesCount) {
    const line = lines[cursor]?.trim() ?? ''
    if (!line.startsWith('@') || line.length === 1) break
    cursor += 1
    stripped += 1
  }

  if (stripped === 0) return text

  while (cursor < lines.length && (lines[cursor]?.trim() ?? '') === '') {
    cursor += 1
  }

  return lines.slice(cursor).join('\n')
}

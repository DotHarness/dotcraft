import type { FileDiff } from '../types/toolCall'

/**
 * Only accurate when the diff covers the entire file, which is the case for
 * WriteFile and for EditFile hunks produced by diffExtractor.ts.
 */
export function reconstructOriginalContent(diff: FileDiff): string {
  if (diff.originalContent !== undefined) return diff.originalContent
  if (diff.isNewFile) return ''

  const lines: string[] = []
  for (const hunk of diff.diffHunks) {
    for (const line of hunk.lines) {
      if (line.type === 'context' || line.type === 'remove') {
        lines.push(line.content)
      }
    }
  }
  return lines.join('\n')
}

/** Same whole-file assumption as {@link reconstructOriginalContent}. */
export function reconstructNewContent(diff: FileDiff): string {
  if (diff.currentContent !== undefined) return diff.currentContent

  const lines: string[] = []
  for (const hunk of diff.diffHunks) {
    for (const line of hunk.lines) {
      if (line.type === 'context' || line.type === 'add') {
        lines.push(line.content)
      }
    }
  }
  return lines.join('\n')
}

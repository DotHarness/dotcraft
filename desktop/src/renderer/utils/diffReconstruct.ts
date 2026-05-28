import type { FileDiff } from '../types/toolCall'

/**
 * Reconstructs the original (pre-edit) file content from a FileDiff.
 *
 * For new files (isNewFile === true), the original content is empty.
 * Otherwise, we walk the diff hunks and collect context lines + removed lines,
 * skipping added lines. This produces the content that existed before the edit.
 *
 * Note: this reconstruction is only accurate when the diff covers the entire
 * file, which is the case for WriteFile (full replacement) and EditFile
 * hunks produced by diffExtractor.ts.
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
      // skip 'add' lines — they did not exist in the original
    }
  }
  return lines.join('\n')
}

/**
 * Reconstructs the new (post-edit) file content from a FileDiff.
 *
 * We walk the diff hunks and collect context lines + added lines,
 * skipping removed lines. This produces the content the agent wrote.
 */
export function reconstructNewContent(diff: FileDiff): string {
  if (diff.currentContent !== undefined) return diff.currentContent

  const lines: string[] = []
  for (const hunk of diff.diffHunks) {
    for (const line of hunk.lines) {
      if (line.type === 'context' || line.type === 'add') {
        lines.push(line.content)
      }
      // skip 'remove' lines — they no longer exist in the new version
    }
  }
  return lines.join('\n')
}

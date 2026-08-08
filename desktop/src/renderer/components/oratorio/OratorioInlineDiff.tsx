import { InlineDiffView } from '../conversation/InlineDiffView'
import type { FileDiff } from '../../types/toolCall'

export function OratorioInlineDiff({ filePath, line, before, after }: {
  filePath: string
  line: number
  before: string
  after: string
}): JSX.Element {
  const removed = before.split('\n')
  const added = after.split('\n')
  const diff: FileDiff = {
    filePath,
    turnId: 'oratorio-review',
    turnIds: ['oratorio-review'],
    additions: added.length,
    deletions: removed.length,
    diffHunks: [{
      oldStart: line,
      oldLines: removed.length,
      newStart: line,
      newLines: added.length,
      lines: [
        ...removed.map((content) => ({ type: 'remove' as const, content })),
        ...added.map((content) => ({ type: 'add' as const, content }))
      ]
    }],
    status: 'written',
    isNewFile: false,
    originalContent: before,
    currentContent: after
  }
  return <InlineDiffView diff={diff} variant="embedded" presentation="body-only" />
}

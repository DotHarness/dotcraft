// Both versions of the file are reconstructed and tokenized separately, so a block
// comment or verbatim string keeps its coloring past its first line. See
// `highlight/diffSides.ts`.
import type { JSX } from 'react'
import { SplitDiffBody } from './diff/SplitDiffBody'
import { UnifiedDiffBody } from './diff/UnifiedDiffBody'
import { toRelativePath } from './diff/diffRows'
import { useDiffModel } from './diff/useDiffModel'
import type { FileDiff } from '../../types/toolCall'

export type DiffDisplayMode = 'inline' | 'split'

interface DiffViewerProps {
  diff: FileDiff
  workspacePath: string
  mode?: DiffDisplayMode
  wordWrap?: boolean
}

export function DiffViewer({
  diff,
  workspacePath,
  mode = 'inline',
  wordWrap = false
}: DiffViewerProps): JSX.Element {
  const relativePath = toRelativePath(diff.filePath, workspacePath)
  const model = useDiffModel(diff)

  return (
    <div
      className="dc-code"
      data-testid="diff-viewer"
      data-mode={mode}
      data-wrap={wordWrap ? 'true' : undefined}
      style={{
        overflow: 'hidden',
        fontFamily: 'var(--font-mono)',
        fontSize: 'var(--text-code-size)',
        lineHeight: 'var(--dc-code-line-height)'
      }}
    >
      {mode === 'split'
        ? <SplitDiffBody diff={diff} model={model} relativePath={relativePath} wordWrap={wordWrap} />
        : <UnifiedDiffBody diff={diff} model={model} relativePath={relativePath} wordWrap={wordWrap} />}
    </div>
  )
}

export { SplitDiffBody } from './diff/SplitDiffBody'
export { UnifiedDiffBody } from './diff/UnifiedDiffBody'

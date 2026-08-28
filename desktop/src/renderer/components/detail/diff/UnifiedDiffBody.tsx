// Not windowed: a diff shows hunks rather than a whole file, and it sits inside
// the changes list's scroller, so a nested scrollbar would cost more than it saves.
import { useMemo, useRef } from 'react'
import { useUIStore } from '../../../stores/uiStore'
import { useFindSurface } from '../../../find/useFindSurface'
import type { FindSegment } from '../../../find/types'
import type { FileDiff } from '../../../types/toolCall'
import { buildUnifiedRows } from './diffRows'
import { DiffContent, DiffGutter, DiffMarker, DiffRowFrame, EmptyDiffMessage, UnchangedDivider } from './DiffRow'
import type { DiffModel } from './useDiffModel'

export interface UnifiedDiffBodyProps {
  diff: FileDiff
  model: DiffModel
  relativePath?: string
  wordWrap?: boolean
}

export function UnifiedDiffBody({
  diff,
  model,
  relativePath,
  wordWrap = false
}: UnifiedDiffBodyProps): JSX.Element {
  const signMode = useUIStore((state) => state.diffMarkers) === 'sign'
  const containerRef = useRef<HTMLDivElement>(null)
  const rows = useMemo(() => buildUnifiedRows(diff, model.sides), [diff, model.sides])

  const segments = useMemo((): FindSegment[] => rows.flatMap((row, index) => row.kind === 'line'
    ? [{ key: `u${index}`, rowIndex: index, lineId: `${index}:${row.cell.side}`, text: row.cell.content }]
    : []), [rows])

  useFindSurface({
    id: diff.diffHunks.length === 0 ? undefined : `diff:${diff.filePath}:unified`,
    domain: 'diff',
    priority: 20,
    getSegments: () => segments,
    getContainer: () => containerRef.current,
    contentKey: model.cacheKey
  })

  if (diff.diffHunks.length === 0) return <EmptyDiffMessage />

  return (
    <div
      ref={containerRef}
      data-testid="unified-diff-body"
      style={{ overflowX: wordWrap ? 'hidden' : 'auto' }}
    >
      <div style={{ minWidth: wordWrap ? undefined : 'max-content' }}>
        {rows.map((row, index) => {
          if (row.kind === 'divider') {
            return <UnchangedDivider key={`divider-${index}`} count={row.count} />
          }
          return (
            <DiffRowFrame key={`line-${index}`} type={row.cell.type} signMode={signMode} wordWrap={wordWrap}>
              <DiffGutter value={row.oldNum} />
              <DiffGutter value={row.newNum} />
              {signMode && <DiffMarker type={row.cell.type} />}
              <DiffContent
                cell={row.cell}
                line={model.lineFor(row.cell)}
                lineId={`${index}:${row.cell.side}`}
                highlighted={model.highlighted}
                title={relativePath}
                wordWrap={wordWrap}
              />
            </DiffRowFrame>
          )
        })}
      </div>
    </div>
  )
}

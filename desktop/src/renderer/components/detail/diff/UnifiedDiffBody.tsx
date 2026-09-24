// Not windowed: a diff shows hunks rather than a whole file, and it sits inside
// the changes list's scroller, so a nested scrollbar would cost more than it saves.
import { Fragment, useMemo, useRef } from 'react'
import { useUIStore } from '../../../stores/uiStore'
import { useFindSurface } from '../../../find/useFindSurface'
import type { FindSegment } from '../../../find/types'
import type { FileDiff } from '../../../types/toolCall'
import { buildUnifiedRows } from './diffRows'
import {
  DiffContent,
  DiffGutter,
  DiffMarker,
  DiffRowFrame,
  EmptyDiffMessage,
  UnchangedDivider,
} from './DiffRow'
import {
  DiffCommentAdd,
  DiffFeedbackGutter,
  DiffLineFeedback,
  useDiffComments,
} from './DiffFeedback'
import { DIFF_GUTTER_WIDTH } from './diffStyles'
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
  wordWrap = false,
}: UnifiedDiffBodyProps): JSX.Element {
  const signMode = useUIStore((state) => state.diffMarkers) === 'sign'
  const containerRef = useRef<HTMLDivElement>(null)
  const comments = useDiffComments()
  const rows = useMemo(
    () => buildUnifiedRows(diff, model.sides),
    [diff, model.sides],
  )

  const segments = useMemo(
    (): FindSegment[] =>
      rows.flatMap((row, index) =>
        row.kind === 'line'
          ? [
              {
                key: `u${index}`,
                rowIndex: index,
                lineId: `${index}:${row.cell.side}`,
                text: row.cell.content,
              },
            ]
          : [],
      ),
    [rows],
  )

  useFindSurface({
    id:
      diff.diffHunks.length === 0 ? undefined : `diff:${diff.filePath}:unified`,
    domain: 'diff',
    priority: 20,
    getSegments: () => segments,
    getContainer: () => containerRef.current,
    contentKey: model.cacheKey,
  })

  if (diff.diffHunks.length === 0) return <EmptyDiffMessage />

  return (
    <div
      ref={containerRef}
      data-testid="unified-diff-body"
      style={{
        overflowX: wordWrap ? 'hidden' : 'auto',
        containerType: 'inline-size',
        ['--dc-line-comment-inset' as string]: `${DIFF_GUTTER_WIDTH * 2 + (signMode ? 16 : 0)}px`,
      }}
    >
      <div style={{ minWidth: wordWrap ? undefined : 'max-content' }}>
        {rows.map((row, index) => {
          if (row.kind === 'divider') {
            return (
              <UnchangedDivider key={`divider-${index}`} count={row.count} />
            )
          }
          const side = row.cell.type === 'remove' ? 'left' : 'right'
          const line = side === 'left' ? row.oldNum : row.newNum
          return (
            <Fragment key={`line-${index}`}>
              <DiffRowFrame
                type={row.cell.type}
                signMode={signMode}
                wordWrap={wordWrap}
                commentTarget={
                  comments && line
                    ? { side, line: Number(line), selected: comments.isSelected(side, Number(line)) }
                    : undefined
                }
              >
                <DiffCommentAdd side={side} line={line} />
                <DiffFeedbackGutter side="left" value={row.oldNum}>
                  <DiffGutter value={row.oldNum} />
                </DiffFeedbackGutter>
                <DiffFeedbackGutter side="right" value={row.newNum}>
                  <DiffGutter value={row.newNum} />
                </DiffFeedbackGutter>
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
              <DiffLineFeedback side="left" line={row.oldNum} />
              <DiffLineFeedback side="right" line={row.newNum} />
            </Fragment>
          )
        })}
      </div>
    </div>
  )
}

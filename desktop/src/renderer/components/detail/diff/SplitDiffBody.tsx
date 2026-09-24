// Each pane scrolls horizontally on its own and the two are kept in step, so a
// long line on one side does not drag the other out of alignment.
import { useMemo, useRef } from 'react'
import { useUIStore } from '../../../stores/uiStore'
import { useFindSurface } from '../../../find/useFindSurface'
import type { FindSegment } from '../../../find/types'
import type { FileDiff } from '../../../types/toolCall'
import { buildSplitRows, type DiffCell, type SplitRow } from './diffRows'
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
  DiffFeedbackRow,
  DiffLineFeedback,
  useDiffComments,
} from './DiffFeedback'
import { DIFF_GUTTER_WIDTH } from './diffStyles'
import type { DiffModel } from './useDiffModel'

export interface SplitDiffBodyProps {
  diff: FileDiff
  model: DiffModel
  relativePath?: string
  wordWrap?: boolean
}

export function SplitDiffBody({
  diff,
  model,
  relativePath,
  wordWrap = false,
}: SplitDiffBodyProps): JSX.Element {
  const leftPaneRef = useRef<HTMLDivElement>(null)
  const rightPaneRef = useRef<HTMLDivElement>(null)
  const containerRef = useRef<HTMLDivElement>(null)
  const rows = useMemo(
    () => buildSplitRows(diff, model.sides),
    [diff, model.sides],
  )

  const segments = useMemo(
    (): FindSegment[] =>
      rows.flatMap((row, index) => {
        if (row.kind !== 'line') return []
        return (
          [
            ['deletion', row.left],
            ['addition', row.right],
          ] as const
        ).flatMap(([side, cell]) =>
          cell.type === 'blank'
            ? []
            : [
                {
                  key: `${index}:${side}`,
                  rowIndex: index,
                  lineId: `${index}:${side}`,
                  scopeSelector: `[data-diff-side="${side}"]`,
                  text: cell.content,
                },
              ],
        )
      }),
    [rows],
  )

  useFindSurface({
    id: diff.diffHunks.length === 0 ? undefined : `diff:${diff.filePath}:split`,
    domain: 'diff',
    priority: 20,
    getSegments: () => segments,
    getContainer: () => containerRef.current,
    contentKey: model.cacheKey,
  })

  if (diff.diffHunks.length === 0) return <EmptyDiffMessage />

  function syncScroll(source: 'left' | 'right'): void {
    if (wordWrap) return
    const from = source === 'left' ? leftPaneRef.current : rightPaneRef.current
    const to = source === 'left' ? rightPaneRef.current : leftPaneRef.current
    if (from === null || to === null || to.scrollLeft === from.scrollLeft)
      return
    to.scrollLeft = from.scrollLeft
  }

  const paneStyle = wordWrap
    ? { minWidth: 0, overflow: 'hidden' as const, containerType: 'inline-size' as const }
    : {
        minWidth: 0,
        overflowX: 'auto' as const,
        overflowY: 'hidden' as const,
        containerType: 'inline-size' as const,
      }

  return (
    <div
      ref={containerRef}
      data-testid="split-diff-body"
      style={{
        display: 'grid',
        minWidth: 0,
        gridTemplateColumns: 'minmax(0, 1fr) minmax(0, 1fr)',
        overflow: 'hidden',
      }}
    >
      <div
        ref={leftPaneRef}
        data-testid="split-left-pane"
        data-diff-side="deletion"
        onScroll={() => syncScroll('left')}
        style={{ ...paneStyle, borderRight: '1px solid var(--border-default)' }}
      >
        <SplitPaneRows
          rows={rows}
          side="deletion"
          model={model}
          title={relativePath}
          wordWrap={wordWrap}
        />
      </div>
      <div
        ref={rightPaneRef}
        data-testid="split-right-pane"
        data-diff-side="addition"
        onScroll={() => syncScroll('right')}
        style={paneStyle}
      >
        <SplitPaneRows
          rows={rows}
          side="addition"
          model={model}
          title={relativePath}
          wordWrap={wordWrap}
        />
      </div>
    </div>
  )
}

function SplitPaneRows({
  rows,
  side,
  model,
  title,
  wordWrap,
}: {
  rows: SplitRow[]
  side: 'deletion' | 'addition'
  model: DiffModel
  title?: string
  wordWrap: boolean
}): JSX.Element {
  const signMode = useUIStore((state) => state.diffMarkers) === 'sign'
  const comments = useDiffComments()
  const commentSide = side === 'deletion' ? 'left' : 'right'
  return (
    <div
      style={{
        minWidth: wordWrap ? undefined : 'max-content',
        ['--dc-line-comment-inset' as string]: `${DIFF_GUTTER_WIDTH + (signMode ? 16 : 0)}px`,
      }}
    >
      {rows.map((row, index) => {
        if (row.kind === 'divider') {
          return <UnchangedDivider key={`divider-${index}`} count={row.count} />
        }
        const cell: DiffCell = side === 'deletion' ? row.left : row.right
        return (
          <DiffFeedbackRow key={`line-${index}`} index={index} side={side}>
            <DiffRowFrame
              type={cell.type}
              signMode={signMode}
              wordWrap={wordWrap}
              style={{
                width: wordWrap ? '100%' : 'max-content',
                minWidth: '100%',
              }}
              commentTarget={
                comments && cell.num
                  ? {
                      side: commentSide,
                      line: Number(cell.num),
                      selected: comments.isSelected(commentSide, Number(cell.num)),
                    }
                  : undefined
              }
            >
              <DiffCommentAdd side={commentSide} line={cell.num} />
              <DiffFeedbackGutter
                side={side === 'deletion' ? 'left' : 'right'}
                value={cell.num}
              >
                <DiffGutter value={cell.num} />
              </DiffFeedbackGutter>
              {signMode && <DiffMarker type={cell.type} />}
              <DiffContent
                cell={cell}
                line={model.lineFor(cell)}
                lineId={`${index}:${side}`}
                highlighted={model.highlighted}
                title={title}
                wordWrap={wordWrap}
              />
            </DiffRowFrame>
            <DiffLineFeedback
              side={side === 'deletion' ? 'left' : 'right'}
              line={cell.num}
            />
          </DiffFeedbackRow>
        )
      })}
    </div>
  )
}

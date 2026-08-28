// `data-line` identifies a rendered row so find can scroll to and paint a result.
import type { CSSProperties, ReactNode } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { LineNumber, LineSpans } from '../../code/CodeSpans'
import codeCss from '../../code/code.module.css'
import type { HighlightedLine } from '../../../highlight'
import type { DiffCell } from './diffRows'
import {
  DIFF_GUTTER_WIDTH,
  diffLineBackground,
  diffLineBar,
  diffLineColor,
  markerStyle,
  markerText
} from './diffStyles'

export interface DiffContentProps {
  cell: DiffCell
  line: HighlightedLine | undefined
  /** Value for `data-line`; unique among rendered rows. */
  lineId: string
  highlighted: boolean
  title?: string
  wordWrap: boolean
}

export function DiffContent({
  cell,
  line,
  lineId,
  highlighted,
  title,
  wordWrap
}: DiffContentProps): JSX.Element {
  return (
    <span
      className={codeCss.content}
      data-line={lineId}
      // Tints the word-level runs. In unified mode there is no pane to carry it.
      data-diff-side={cell.side}
      data-testid={highlighted ? 'highlighted-diff-line' : undefined}
      title={title}
      style={{
        color: diffLineColor(cell.type),
        ...(wordWrap ? wrapStyle : null)
      }}
    >
      {cell.type === 'blank' ? ' ' : <LineSpans line={line} text={cell.content} />}
    </span>
  )
}

export interface DiffRowFrameProps {
  type: DiffCell['type']
  signMode: boolean
  wordWrap: boolean
  style?: CSSProperties
  children: ReactNode
}

export function DiffRowFrame({
  type,
  signMode,
  wordWrap,
  style,
  children
}: DiffRowFrameProps): JSX.Element {
  return (
    <div
      style={{
        display: 'flex',
        minWidth: wordWrap ? undefined : 'max-content',
        background: type === 'blank' ? 'var(--bg-primary)' : diffLineBackground(type),
        boxShadow: type === 'blank' ? undefined : diffLineBar(type, signMode),
        whiteSpace: wordWrap ? 'pre-wrap' : 'pre',
        ...style
      }}
    >
      {children}
    </div>
  )
}

export function DiffGutter({ value }: { value: string }): JSX.Element {
  return <LineNumber value={value} width={DIFF_GUTTER_WIDTH} />
}

export function DiffMarker({ type }: { type: DiffCell['type'] }): JSX.Element {
  return <span style={markerStyle(type)} data-find-skip>{markerText(type)}</span>
}

export function UnchangedDivider({ count }: { count: number }): JSX.Element {
  const t = useT()
  return (
    <div style={dividerStyle} data-find-skip>
      {t('diffViewer.unchangedLines', { count })}
    </div>
  )
}

export function EmptyDiffMessage(): JSX.Element {
  const t = useT()
  return (
    <div style={{ padding: '10px 12px', color: 'var(--text-dimmed)', fontSize: '12px' }}>
      {t('diffViewer.noChanges')}
    </div>
  )
}

const dividerStyle: CSSProperties = {
  padding: '4px 8px',
  color: 'var(--text-dimmed)',
  background: 'var(--bg-secondary)',
  fontSize: '11px',
  userSelect: 'none'
}

const wrapStyle: CSSProperties = {
  flex: 1,
  minWidth: 0,
  overflowWrap: 'anywhere',
  whiteSpace: 'pre-wrap',
  wordBreak: 'break-word'
}

// `data-line-num` marks a gutter as chrome, so a search for "12" does not match
// every twelfth line number.
import { memo } from 'react'
import type { HighlightedLine } from '../../highlight'
import css from './code.module.css'

export interface LineNumberProps {
  value: number | string
  width?: number
  title?: string
}

export function LineNumber({ value, width, title }: LineNumberProps): JSX.Element {
  return (
    <span
      className={css.gutter}
      data-line-num=""
      title={title}
      aria-hidden
      style={width === undefined ? undefined : { width }}
    >
      {value}
    </span>
  )
}

export interface LineSpansProps {
  line: HighlightedLine | undefined
  /** Rendered as-is while a grammar loads, or when none applies. */
  text: string
}

export const LineSpans = memo(function LineSpans({ line, text }: LineSpansProps): JSX.Element {
  if (line === undefined) return <>{text}</>
  return (
    <>
      {line.map((span, index) => (
        <span
          key={index}
          style={span.style}
          {...(span.changed === true ? { 'data-diff-span': '' } : {})}
        >
          {span.text}
        </span>
      ))}
    </>
  )
})

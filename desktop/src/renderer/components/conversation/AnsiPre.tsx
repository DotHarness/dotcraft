import { useMemo, type CSSProperties } from 'react'
import { parseAnsi, type AnsiSpan } from '../../utils/ansi'

interface AnsiPreProps {
  text: string
  maxHeight?: number
  colorWhenNoSgr?: string
  truncatedLinesOver?: number
}

export function AnsiPre({
  text,
  maxHeight,
  colorWhenNoSgr = 'var(--text-secondary)',
  truncatedLinesOver
}: AnsiPreProps): JSX.Element {
  const renderedNodes = useMemo(() => {
    const spans = parseAnsi(text)
    const totalLines = text.split('\n').length
    const shouldTruncate = truncatedLinesOver != null && totalLines > truncatedLinesOver
    const lineLimit = shouldTruncate ? truncatedLinesOver : undefined
    const nodes: Array<JSX.Element | string> = []
    let currentLine = 0

    outer: for (const span of spans) {
      const parts = span.text.split('\n')
      for (let idx = 0; idx < parts.length; idx++) {
        if (idx > 0) {
          if (lineLimit != null && currentLine + 1 >= lineLimit) {
            break outer
          }
          nodes.push('\n')
          currentLine++
        }

        if (parts[idx].length === 0) continue
        nodes.push(
          <span
            key={`ansi-${currentLine}-${nodes.length}`}
            style={resolveSpanStyle(span, colorWhenNoSgr)}
          >
            {parts[idx]}
          </span>
        )
      }
    }

    if (shouldTruncate && lineLimit != null) {
      if (lineLimit > 0) {
        nodes.push('\n')
      }
      nodes.push(<span key="ansi-truncation-ellipsis">…</span>)
    }
    return nodes
  }, [colorWhenNoSgr, text, truncatedLinesOver])

  return (
    <pre
      style={{
        margin: 0,
        whiteSpace: 'pre-wrap',
        wordBreak: 'break-all',
        overflow: 'auto',
        maxHeight: maxHeight != null ? `${maxHeight}px` : undefined,
        fontFamily: 'var(--font-mono)',
        color: colorWhenNoSgr
      }}
    >
      {renderedNodes}
    </pre>
  )
}

function resolveSpanStyle(
  span: AnsiSpan,
  colorWhenNoSgr: string
): CSSProperties {
  const fg = span.inverse ? (span.bg ?? colorWhenNoSgr) : (span.fg ?? colorWhenNoSgr)
  const bg = span.inverse ? (span.fg ?? colorWhenNoSgr) : span.bg
  const decorations = [
    span.underline ? 'underline' : '',
    span.strike ? 'line-through' : ''
  ].filter((value) => value.length > 0).join(' ')

  return {
    color: fg,
    backgroundColor: bg,
    fontWeight: span.bold ? 600 : undefined,
    opacity: span.dim ? 0.65 : undefined,
    fontStyle: span.italic ? 'italic' : undefined,
    textDecoration: decorations || undefined
  }
}

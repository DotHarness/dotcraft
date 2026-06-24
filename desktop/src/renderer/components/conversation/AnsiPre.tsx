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
    const truncated = truncateTextByLines(text, truncatedLinesOver)
    const spans = parseAnsi(truncated.text)
    const nodes: Array<JSX.Element | string> = []
    let currentLine = 0

    for (const span of spans) {
      const parts = span.text.split('\n')
      for (let idx = 0; idx < parts.length; idx++) {
        if (idx > 0) {
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

    if (truncated.truncated) {
      if (truncated.text.length > 0) {
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

function truncateTextByLines(
  text: string,
  lineLimit: number | undefined
): { text: string; truncated: boolean } {
  if (lineLimit == null) {
    return { text, truncated: false }
  }
  if (lineLimit <= 0) {
    return { text: '', truncated: text.length > 0 }
  }

  let linesSeen = 1
  for (let index = 0; index < text.length; index++) {
    if (text.charCodeAt(index) !== 10) continue
    linesSeen++
    if (linesSeen > lineLimit) {
      return { text: text.slice(0, index), truncated: true }
    }
  }

  return { text, truncated: false }
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

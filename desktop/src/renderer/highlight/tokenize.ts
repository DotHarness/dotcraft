import type { HighlighterCore } from 'shiki/core'
import { TOKEN_OPTIONS, type HighlightedLine, type HighlightSegment, type HighlightSpan } from './types'

/** shiki splits on LF only; a stray `\r` would otherwise end every line's last token. */
export function normalizeNewlines(text: string): string {
  return text.includes('\r') ? text.replace(/\r\n?/g, '\n') : text
}

/** A trailing newline yields a final empty line, matching what shiki emits. */
export function splitLines(text: string): string[] {
  return text.split('\n')
}

export function plainLine(text: string): HighlightedLine {
  return text.length === 0 ? [] : [{ text }]
}

export function plainLines(text: string): HighlightedLine[] {
  return splitLines(normalizeNewlines(text)).map(plainLine)
}

export function tokenizeText(
  highlighter: HighlighterCore,
  text: string,
  lang: string
): HighlightedLine[] {
  const normalized = normalizeNewlines(text)
  const { tokens } = highlighter.codeToTokens(normalized, { lang, ...TOKEN_OPTIONS })
  return tokens.map((line) => line.map(toSpan))
}

function toSpan(token: { content: string; htmlStyle?: Record<string, string> | string }): HighlightSpan {
  // The string form only appears under a single resolved theme, which this app never configures.
  return typeof token.htmlStyle === 'object' && token.htmlStyle !== null
    ? { text: token.content, style: token.htmlStyle }
    : { text: token.content }
}

/**
 * Each segment is tokenized in one call so multi-line constructs stay intact;
 * `lineIndices` then picks the rows to keep, in display order.
 */
export function tokenizeSegments(
  highlighter: HighlighterCore,
  segments: HighlightSegment[],
  lang: string
): HighlightedLine[] {
  const rows: HighlightedLine[] = []
  for (const segment of segments) {
    const lines = tokenizeText(highlighter, segment.text, lang)
    for (const index of segment.lineIndices) rows.push(lines[index] ?? [])
  }
  return rows
}

export function plainSegments(segments: HighlightSegment[]): HighlightedLine[] {
  const rows: HighlightedLine[] = []
  for (const segment of segments) {
    const lines = splitLines(normalizeNewlines(segment.text))
    for (const index of segment.lineIndices) rows.push(plainLine(lines[index] ?? ''))
  }
  return rows
}

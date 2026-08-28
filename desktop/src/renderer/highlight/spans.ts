import type { CharRange, HighlightedLine, HighlightSpan } from './types'

/** Cuts tokens at diff-mark boundaries, so the marks need no second overlay layer. */
export function applyChangeRanges(
  line: HighlightedLine,
  ranges: CharRange[] | undefined
): HighlightedLine {
  if (ranges === undefined || ranges.length === 0 || line.length === 0) return line

  const result: HighlightSpan[] = []
  let offset = 0
  let rangeIndex = 0

  for (const span of line) {
    const spanStart = offset
    const spanEnd = offset + span.text.length
    offset = spanEnd

    // Ranges are ordered, so anything ending before this span is already spent.
    while (rangeIndex < ranges.length && ranges[rangeIndex].end <= spanStart) {
      rangeIndex++
    }

    let cursor = spanStart
    let scan = rangeIndex
    while (cursor < spanEnd && scan < ranges.length) {
      const range = ranges[scan]
      if (range.start >= spanEnd) break
      const start = Math.max(range.start, cursor)
      const end = Math.min(range.end, spanEnd)
      if (start > cursor) push(result, span, cursor - spanStart, start - spanStart, false)
      if (end > start) push(result, span, start - spanStart, end - spanStart, true)
      cursor = end
      if (range.end <= spanEnd) scan++
      else break
    }

    if (cursor < spanEnd) push(result, span, cursor - spanStart, spanEnd - spanStart, false)
  }

  return result
}

function push(
  target: HighlightSpan[],
  span: HighlightSpan,
  from: number,
  to: number,
  changed: boolean
): void {
  if (to <= from) return
  const text = span.text.slice(from, to)
  target.push(changed ? { ...span, text, changed: true } : { ...span, text })
}

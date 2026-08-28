// Substring matching, not regex: queries are typed while reading code, where `*` and
// `(` are far more often literal than they are operators.
import {
  FIND_CONTEXT_LENGTH,
  MAX_FIND_MATCHES,
  type FindDomain,
  type FindMatch,
  type FindMatchContext,
  type FindSegment
} from './types'

export interface FindResult {
  matches: FindMatch[]
  /** Every occurrence, including any beyond {@link MAX_FIND_MATCHES}. */
  totalMatches: number
  isCapped: boolean
}

export const EMPTY_FIND_RESULT: FindResult = { matches: [], totalMatches: 0, isCapped: false }

export function findOffsets(
  text: string,
  query: string,
  limit: number
): { offsets: { start: number; end: number }[]; totalMatches: number; isCapped: boolean } {
  const offsets: { start: number; end: number }[] = []
  if (query.length === 0) return { offsets, totalMatches: 0, isCapped: false }

  const haystack = text.toLowerCase()
  const needle = query.toLowerCase()
  let total = 0
  let capped = false
  let cursor = 0

  while (cursor <= haystack.length - needle.length) {
    const start = haystack.indexOf(needle, cursor)
    if (start === -1) break
    total++
    if (offsets.length < limit) offsets.push({ start, end: start + needle.length })
    else capped = true
    // Non-overlapping, so "aa" in "aaa" is one match, matching what a reader counts.
    cursor = start + needle.length
  }

  return { offsets, totalMatches: total, isCapped: capped }
}

export function matchContext(text: string, start: number, end: number): FindMatchContext {
  return {
    before: text.slice(Math.max(0, start - FIND_CONTEXT_LENGTH), start),
    match: text.slice(start, end),
    after: text.slice(end, Math.min(text.length, end + FIND_CONTEXT_LENGTH))
  }
}

export function findInSegments(
  surfaceId: string,
  domain: FindDomain,
  segments: FindSegment[],
  query: string,
  limit: number = MAX_FIND_MATCHES
): FindResult {
  if (query.length === 0) return EMPTY_FIND_RESULT

  const matches: FindMatch[] = []
  let totalMatches = 0
  let isCapped = false

  for (const segment of segments) {
    const remaining = Math.max(0, limit - matches.length)
    const found = findOffsets(segment.text, query, remaining)
    totalMatches += found.totalMatches
    if (found.isCapped) isCapped = true

    found.offsets.forEach((offset, occurrence) => {
      matches.push({
        id: `${surfaceId}:${segment.key}:${offset.start}`,
        surfaceId,
        domain,
        segmentKey: segment.key,
        rowIndex: segment.rowIndex,
        lineId: segment.lineId,
        scopeSelector: segment.scopeSelector,
        start: offset.start,
        end: offset.end,
        occurrence,
        context: matchContext(segment.text, offset.start, offset.end)
      })
    })
  }

  return { matches, totalMatches, isCapped }
}

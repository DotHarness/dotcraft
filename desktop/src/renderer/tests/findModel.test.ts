import { describe, expect, it } from 'vitest'
import { findInSegments, findOffsets, matchContext } from '../find/model'
import type { FindSegment } from '../find/types'

const SEGMENTS: FindSegment[] = [
  { key: '0', rowIndex: 0, lineId: '1', text: 'const total = 1' },
  { key: '1', rowIndex: 1, lineId: '2', text: 'return total + total' },
  { key: '2', rowIndex: 2, lineId: '3', text: 'done' }
]

describe('findOffsets', () => {
  it('is case-insensitive', () => {
    expect(findOffsets('Total', 'total', 10).offsets).toEqual([{ start: 0, end: 5 }])
  })

  it('counts non-overlapping occurrences the way a reader would', () => {
    expect(findOffsets('aaa', 'aa', 10).totalMatches).toBe(1)
  })

  it('reports the true total even past the collection limit', () => {
    const result = findOffsets('x x x x', 'x', 2)
    expect(result.offsets).toHaveLength(2)
    expect(result.totalMatches).toBe(4)
    expect(result.isCapped).toBe(true)
  })

  it('finds nothing for an empty query', () => {
    expect(findOffsets('anything', '', 10)).toEqual({ offsets: [], totalMatches: 0, isCapped: false })
  })
})

describe('matchContext', () => {
  it('keeps the surrounding text for a result preview', () => {
    const text = 'the quick brown fox jumps'
    expect(matchContext(text, 10, 15)).toEqual({
      before: 'the quick ',
      match: 'brown',
      after: ' fox jumps'
    })
  })
})

describe('findInSegments', () => {
  it('returns matches in display order, each locatable in its own row', () => {
    const result = findInSegments('file:a', 'file', SEGMENTS, 'total')

    expect(result.totalMatches).toBe(3)
    expect(result.matches.map((match) => match.segmentKey)).toEqual(['0', '1', '1'])
    expect(result.matches.map((match) => match.rowIndex)).toEqual([0, 1, 1])
    // `occurrence` is what distinguishes two matches sharing a row.
    expect(result.matches.map((match) => match.occurrence)).toEqual([0, 0, 1])
    expect(new Set(result.matches.map((match) => match.id)).size).toBe(3)
  })

  it('carries the surface and domain so cross-surface results stay attributable', () => {
    const result = findInSegments('diff:b', 'diff', SEGMENTS, 'done')
    expect(result.matches[0]).toMatchObject({ surfaceId: 'diff:b', domain: 'diff', lineId: '3' })
  })

  it('stops collecting at the shared budget but still counts', () => {
    const result = findInSegments('file:a', 'file', SEGMENTS, 'total', 1)
    expect(result.matches).toHaveLength(1)
    expect(result.totalMatches).toBe(3)
    expect(result.isCapped).toBe(true)
  })
})

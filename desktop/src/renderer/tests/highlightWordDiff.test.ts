import { describe, expect, it } from 'vitest'
import { lineChangeRanges, pairChangedLines } from '../highlight/wordDiff'
import { applyChangeRanges } from '../highlight/spans'
import type { HighlightedLine } from '../highlight/types'

function marked(line: HighlightedLine): string[] {
  return line.filter((span) => span.changed === true).map((span) => span.text)
}

describe('lineChangeRanges', () => {
  it('marks only the words that differ', () => {
    const ranges = lineChangeRanges('const value = 1', 'const value = 2')
    expect(ranges).toBeDefined()
    const before = 'const value = 1'
    const after = 'const value = 2'
    expect(ranges?.deletion.map((range) => before.slice(range.start, range.end))).toEqual(['1'])
    expect(ranges?.addition.map((range) => after.slice(range.start, range.end))).toEqual(['2'])
  })

  it('absorbs a one-character gap so a renamed member is one mark, not two', () => {
    const before = 'a.b()'
    const after = 'a.c()'
    const ranges = lineChangeRanges(before, after)
    // Without the gap rule the dot would split `b` and `(` into separate marks.
    expect(ranges?.addition.length).toBe(1)
    expect(after.slice(ranges?.addition[0]?.start, ranges?.addition[0]?.end)).toContain('c')
  })

  it('returns no ranges for identical lines', () => {
    expect(lineChangeRanges('same', 'same')).toEqual({ deletion: [], addition: [] })
  })

  it('declines lines longer than the cap rather than diffing a minified bundle', () => {
    const long = 'x'.repeat(1001)
    expect(lineChangeRanges(long, `${long}y`)).toBeUndefined()
  })
})

describe('pairChangedLines', () => {
  it('pairs a run of removals with the run of additions that replaced it', () => {
    const hunk = {
      oldStart: 1,
      oldLines: 3,
      newStart: 1,
      newLines: 3,
      lines: [
        { type: 'context' as const, content: 'a' },
        { type: 'remove' as const, content: 'b' },
        { type: 'remove' as const, content: 'c' },
        { type: 'add' as const, content: 'B' },
        { type: 'add' as const, content: 'C' },
        { type: 'context' as const, content: 'd' }
      ]
    }
    expect(pairChangedLines(hunk)).toEqual([
      { removeIndex: 1, addIndex: 3 },
      { removeIndex: 2, addIndex: 4 }
    ])
  })

  it('leaves an unmatched removal unpaired', () => {
    const hunk = {
      oldStart: 1,
      oldLines: 2,
      newStart: 1,
      newLines: 1,
      lines: [
        { type: 'remove' as const, content: 'b' },
        { type: 'remove' as const, content: 'c' },
        { type: 'add' as const, content: 'B' }
      ]
    }
    expect(pairChangedLines(hunk)).toEqual([{ removeIndex: 0, addIndex: 2 }])
  })
})

describe('applyChangeRanges', () => {
  const line: HighlightedLine = [
    { text: 'const ', style: { '--dc-token-light': '#a' } },
    { text: 'value', style: { '--dc-token-light': '#b' } },
    { text: ' = 1', style: { '--dc-token-light': '#c' } }
  ]

  it('splits a syntax run at the change boundary and keeps its color', () => {
    const result = applyChangeRanges(line, [{ start: 6, end: 8 }])
    expect(result.map((span) => span.text).join('')).toBe('const value = 1')
    expect(marked(result)).toEqual(['va'])
    expect(result.find((span) => span.text === 'va')?.style).toEqual({ '--dc-token-light': '#b' })
  })

  it('marks a range spanning several runs', () => {
    const result = applyChangeRanges(line, [{ start: 3, end: 12 }])
    expect(marked(result).join('')).toBe('st value ')
    expect(result.map((span) => span.text).join('')).toBe('const value = 1')
  })

})

import { describe, expect, it } from 'vitest'
import { lineChangeRanges, pairChangedLines } from '../highlight/wordDiff'
import { applyChangeRanges } from '../highlight/spans'
import type { HighlightedLine } from '../highlight/types'

function marked(line: HighlightedLine): string[] {
  return line.filter((span) => span.changed === true).map((span) => span.text)
}

describe('lineChangeRanges', () => {
  it('marks only the words that differ and absorbs a one-character gap into one mark', () => {
    const before = 'const value = 1'
    const after = 'const value = 2'
    const ranges = lineChangeRanges(before, after)
    expect(ranges?.deletion.map((range) => before.slice(range.start, range.end))).toEqual(['1'])
    expect(ranges?.addition.map((range) => after.slice(range.start, range.end))).toEqual(['2'])

    const renamed = lineChangeRanges('a.b()', 'a.c()')
    expect(renamed?.addition.length).toBe(1)
    expect('a.c()'.slice(renamed?.addition[0]?.start, renamed?.addition[0]?.end)).toContain('c')
  })

  it('returns no ranges for identical lines and declines lines longer than the cap', () => {
    expect(lineChangeRanges('same', 'same')).toEqual({ deletion: [], addition: [] })
    const long = 'x'.repeat(1001)
    expect(lineChangeRanges(long, `${long}y`)).toBeUndefined()
  })
})

describe('pairChangedLines', () => {
  it('pairs runs of removals with the additions that replaced them and leaves extras unpaired', () => {
    expect(pairChangedLines({
      oldStart: 1,
      oldLines: 3,
      newStart: 1,
      newLines: 3,
      lines: [
        { type: 'context', content: 'a' },
        { type: 'remove', content: 'b' },
        { type: 'remove', content: 'c' },
        { type: 'add', content: 'B' },
        { type: 'add', content: 'C' },
        { type: 'context', content: 'd' }
      ]
    })).toEqual([
      { removeIndex: 1, addIndex: 3 },
      { removeIndex: 2, addIndex: 4 }
    ])

    expect(pairChangedLines({
      oldStart: 1,
      oldLines: 2,
      newStart: 1,
      newLines: 1,
      lines: [
        { type: 'remove', content: 'b' },
        { type: 'remove', content: 'c' },
        { type: 'add', content: 'B' }
      ]
    })).toEqual([{ removeIndex: 0, addIndex: 2 }])
  })
})

describe('applyChangeRanges', () => {
  const line: HighlightedLine = [
    { text: 'const ', style: { '--dc-token-light': '#a' } },
    { text: 'value', style: { '--dc-token-light': '#b' } },
    { text: ' = 1', style: { '--dc-token-light': '#c' } }
  ]

  it('splits syntax runs at change boundaries, keeping their colors, including across several runs', () => {
    const result = applyChangeRanges(line, [{ start: 6, end: 8 }])
    expect(result.map((span) => span.text).join('')).toBe('const value = 1')
    expect(marked(result)).toEqual(['va'])
    expect(result.find((span) => span.text === 'va')?.style).toEqual({ '--dc-token-light': '#b' })

    const spanning = applyChangeRanges(line, [{ start: 3, end: 12 }])
    expect(marked(spanning).join('')).toBe('st value ')
    expect(spanning.map((span) => span.text).join('')).toBe('const value = 1')
  })
})

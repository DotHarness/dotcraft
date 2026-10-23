import { describe, expect, it } from 'vitest'
import { buildDiffSides } from '../highlight/diffSides'
import { NO_ROW } from '../highlight'
import type { FileDiff } from '../types/toolCall'

function diff(partial: Partial<FileDiff>): FileDiff {
  return {
    filePath: 'F:/work/src/Sample.cs',
    additions: 1,
    deletions: 1,
    status: 'written',
    isNewFile: false,
    diffHunks: [],
    ...partial
  }
}

const HUNK = {
  oldStart: 2,
  oldLines: 3,
  newStart: 2,
  newLines: 3,
  lines: [
    { type: 'context' as const, content: 'const a = 1' },
    { type: 'remove' as const, content: 'const b = 2' },
    { type: 'add' as const, content: 'const b = 3' },
    { type: 'context' as const, content: 'const c = 4' }
  ]
}

describe('buildDiffSides', () => {
  it('rebuilds each side from the whole-file contents and maps hunk lines to file rows', () => {
    const original = 'header\nconst a = 1\nconst b = 2\nconst c = 4\n'
    const current = 'header\nconst a = 1\nconst b = 3\nconst c = 4\n'
    const sides = buildDiffSides(diff({
      diffHunks: [HUNK],
      originalContent: original,
      currentContent: current
    }))

    expect(sides.exact).toBe(true)
    expect(sides.deletion).toHaveLength(1)
    expect(sides.addition).toHaveLength(1)
    expect(sides.deletion[0]?.text).toBe(original)
    expect(sides.addition[0]?.text).toBe(current)
    expect(sides.deletion[0]?.lineIndices).toEqual([1, 2, 3])
    expect(sides.addition[0]?.lineIndices).toEqual([1, 2, 3])
    expect(sides.deletionText).toEqual(['const a = 1', 'const b = 2', 'const c = 4'])
    expect(sides.additionText).toEqual(['const a = 1', 'const b = 3', 'const c = 4'])
    expect(sides.deletionRow[0]).toEqual([0, 1, NO_ROW, 2])
    expect(sides.additionRow[0]).toEqual([0, NO_ROW, 1, 2])
  })

  it('falls back to per-hunk segments when the whole-file contents are missing or disagree with the hunks', () => {
    const missing = buildDiffSides(diff({ diffHunks: [HUNK, HUNK] }))

    expect(missing.exact).toBe(false)
    expect(missing.deletion).toHaveLength(2)
    expect(missing.addition).toHaveLength(2)
    expect(missing.deletion[0]?.text).toBe('const a = 1\nconst b = 2\nconst c = 4')
    expect(missing.addition[0]?.text).toBe('const a = 1\nconst b = 3\nconst c = 4')
    expect(missing.deletion[0]?.lineIndices).toEqual([0, 1, 2])

    const stale = buildDiffSides(diff({
      diffHunks: [HUNK],
      originalContent: 'header\nsomething else entirely\n',
      currentContent: 'header\nconst a = 1\nconst b = 3\nconst c = 4\n'
    }))
    expect(stale.exact).toBe(false)
    expect(stale.deletion[0]?.text).toBe('const a = 1\nconst b = 2\nconst c = 4')
  })

  it('normalizes CRLF so line indices mean the same thing everywhere', () => {
    const sides = buildDiffSides(diff({
      diffHunks: [HUNK],
      originalContent: 'header\r\nconst a = 1\r\nconst b = 2\r\nconst c = 4\r\n',
      currentContent: 'header\r\nconst a = 1\r\nconst b = 3\r\nconst c = 4\r\n'
    }))

    expect(sides.exact).toBe(true)
    expect(sides.deletion[0]?.text).not.toContain('\r')
  })
})

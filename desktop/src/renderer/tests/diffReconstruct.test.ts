import { describe, it, expect } from 'vitest'
import { reconstructOriginalContent, reconstructNewContent } from '../utils/diffReconstruct'
import type { FileDiff, DiffHunk } from '../types/toolCall'

function makeFileDiff(overrides: Partial<FileDiff> = {}): FileDiff {
  return {
    filePath: 'src/test.ts',
    turnId: 'turn-1',
    turnIds: ['turn-1'],
    additions: 0,
    deletions: 0,
    diffHunks: [],
    status: 'written',
    isNewFile: false,
    ...overrides
  }
}

function makeHunk(lines: Array<{ type: 'context' | 'add' | 'remove'; content: string }>): DiffHunk {
  let oldLines = 0, newLines = 0
  for (const l of lines) {
    if (l.type === 'context') { oldLines++; newLines++ }
    else if (l.type === 'remove') oldLines++
    else newLines++
  }
  return { oldStart: 1, oldLines, newStart: 1, newLines, lines }
}

describe('reconstructOriginalContent', () => {
  it('returns originalContent when explicitly set', () => {
    const diff = makeFileDiff({
      originalContent: 'explicit-original',
      isNewFile: false,
      diffHunks: []
    })
    expect(reconstructOriginalContent(diff)).toBe('explicit-original')
  })

  it('returns empty string for new files', () => {
    const diff = makeFileDiff({
      isNewFile: true,
      diffHunks: [makeHunk([
        { type: 'add', content: 'line 1' },
        { type: 'add', content: 'line 2' }
      ])]
    })
    expect(reconstructOriginalContent(diff)).toBe('')
  })

  it('reconstructs original content from an edit diff', () => {
    const diff = makeFileDiff({
      diffHunks: [makeHunk([
        { type: 'context', content: 'const x = 1' },
        { type: 'remove', content: 'const y = 2' },
        { type: 'add', content: 'const y = 99' },
        { type: 'context', content: 'export { x, y }' }
      ])]
    })
    const result = reconstructOriginalContent(diff)
    expect(result).toBe('const x = 1\nconst y = 2\nexport { x, y }')
  })

  it('skips add lines and keeps context + remove', () => {
    const diff = makeFileDiff({
      diffHunks: [makeHunk([
        { type: 'add', content: 'new line A' },
        { type: 'remove', content: 'old line B' },
        { type: 'context', content: 'context line C' }
      ])]
    })
    expect(reconstructOriginalContent(diff)).toBe('old line B\ncontext line C')
  })

  it('handles empty diffHunks', () => {
    const diff = makeFileDiff({ diffHunks: [] })
    expect(reconstructOriginalContent(diff)).toBe('')
  })
})

describe('reconstructNewContent', () => {
  it('returns currentContent when explicitly set', () => {
    const diff = makeFileDiff({
      currentContent: 'explicit-new',
      diffHunks: []
    })
    expect(reconstructNewContent(diff)).toBe('explicit-new')
  })

  it('reconstructs new content from a new file diff', () => {
    const diff = makeFileDiff({
      isNewFile: true,
      diffHunks: [makeHunk([
        { type: 'add', content: 'line 1' },
        { type: 'add', content: 'line 2' },
        { type: 'add', content: 'line 3' }
      ])]
    })
    expect(reconstructNewContent(diff)).toBe('line 1\nline 2\nline 3')
  })

  it('reconstructs new content from an edit diff', () => {
    const diff = makeFileDiff({
      diffHunks: [makeHunk([
        { type: 'context', content: 'const x = 1' },
        { type: 'remove', content: 'const y = 2' },
        { type: 'add', content: 'const y = 99' },
        { type: 'context', content: 'export { x, y }' }
      ])]
    })
    expect(reconstructNewContent(diff)).toBe('const x = 1\nconst y = 99\nexport { x, y }')
  })

  it('skips remove lines and keeps context + add', () => {
    const diff = makeFileDiff({
      diffHunks: [makeHunk([
        { type: 'add', content: 'new line A' },
        { type: 'remove', content: 'old line B' },
        { type: 'context', content: 'context line C' }
      ])]
    })
    expect(reconstructNewContent(diff)).toBe('new line A\ncontext line C')
  })

  it('handles empty diffHunks', () => {
    const diff = makeFileDiff({ diffHunks: [] })
    expect(reconstructNewContent(diff)).toBe('')
  })

  it('handles multiple hunks', () => {
    const diff = makeFileDiff({
      diffHunks: [
        makeHunk([
          { type: 'context', content: 'line 1' },
          { type: 'add', content: 'line 2 new' }
        ]),
        makeHunk([
          { type: 'context', content: 'line 10' },
          { type: 'remove', content: 'line 11 old' },
          { type: 'add', content: 'line 11 new' }
        ])
      ]
    })
    expect(reconstructNewContent(diff)).toBe('line 1\nline 2 new\nline 10\nline 11 new')
  })
})

import { describe, it, expect } from 'vitest'
import {
  extractDiffFromWriteFile,
  extractDiffFromEditFile,
  computeDiffHunks,
  parseResultPath,
  mergeFileDiffIncrement,
  computeCumulativeFileDiff,
  computeIncrementalPerItemDiff,
  reverseEditReplace,
  readWorkspaceAfterTool
} from '../utils/diffExtractor'

describe('parseResultPath', () => {
  it('extracts path from WriteFile result "... to src/foo.ts"', () => {
    expect(parseResultPath('Successfully wrote 100 bytes to src/foo.ts')).toBe('src/foo.ts')
  })

  it('extracts path from EditFile result "Successfully edited src/bar.ts"', () => {
    expect(parseResultPath('Successfully edited src/bar.ts')).toBe('src/bar.ts')
  })

  it('returns null for unrecognized text', () => {
    expect(parseResultPath('some random text')).toBeNull()
  })
})

describe('extractDiffFromWriteFile', () => {
  it('produces a new-file diff with all lines as additions', () => {
    const args = { path: 'src/new.ts', content: 'line1\nline2\nline3' }
    const diff = extractDiffFromWriteFile(args, '', 'turn-1')

    expect(diff).not.toBeNull()
    expect(diff!.filePath).toBe('src/new.ts')
    expect(diff!.turnId).toBe('turn-1')
    expect(diff!.turnIds).toEqual(['turn-1'])
    expect(diff!.isNewFile).toBe(true)
    expect(diff!.status).toBe('written')
    expect(diff!.additions).toBe(3)
    expect(diff!.deletions).toBe(0)
    expect(diff!.diffHunks).toHaveLength(1)
    expect(diff!.diffHunks[0].lines.every((l) => l.type === 'add')).toBe(true)
  })

  it('strips trailing empty line from content', () => {
    const args = { path: 'src/foo.ts', content: 'line1\nline2\n' }
    const diff = extractDiffFromWriteFile(args, '', 'turn-1')

    expect(diff!.additions).toBe(2)
  })

  it('returns null when neither path arg nor result text has a path', () => {
    const diff = extractDiffFromWriteFile({}, 'unknown output', 'turn-1')
    expect(diff).toBeNull()
  })

  it('falls back to path from result text when args.path is missing', () => {
    const args = { content: 'hello' }
    const diff = extractDiffFromWriteFile(args, 'Successfully wrote 5 bytes to fallback.ts', 'turn-1')
    expect(diff!.filePath).toBe('fallback.ts')
  })
})

describe('extractDiffFromEditFile', () => {
  it('produces a diff with correct addition and deletion counts', () => {
    const args = {
      path: 'src/edit.ts',
      oldText: 'line1\nold-line\nline3\n',
      newText: 'line1\nnew-line\nline3\n'
    }
    const diff = extractDiffFromEditFile(args, '', 'turn-2')

    expect(diff).not.toBeNull()
    expect(diff!.filePath).toBe('src/edit.ts')
    expect(diff!.turnIds).toEqual(['turn-2'])
    expect(diff!.isNewFile).toBe(false)
    expect(diff!.additions).toBe(1)
    expect(diff!.deletions).toBe(1)
    expect(diff!.status).toBe('written')
  })

  it('returns null when both oldText and newText are empty', () => {
    const args = { path: 'src/foo.ts' }
    const diff = extractDiffFromEditFile(args, '', 'turn-1')
    expect(diff).toBeNull()
  })

  it('handles adding lines (no deletions)', () => {
    const args = {
      path: 'src/add.ts',
      oldText: 'a\nb\n',
      newText: 'a\nb\nc\n'
    }
    const diff = extractDiffFromEditFile(args, '', 'turn-3')
    expect(diff!.additions).toBe(1)
    expect(diff!.deletions).toBe(0)
  })

  it('handles removing lines (no additions)', () => {
    const args = {
      path: 'src/remove.ts',
      oldText: 'a\nb\nc\n',
      newText: 'a\nc\n'
    }
    const diff = extractDiffFromEditFile(args, '', 'turn-4')
    expect(diff!.additions).toBe(0)
    expect(diff!.deletions).toBe(1)
  })
})

describe('computeIncrementalPerItemDiff', () => {
  it('delegates EditFile to extractDiffFromEditFile even when cumulative exists', () => {
    const cumulative: import('../types/toolCall').FileDiff = {
      filePath: 'src/a.ts',
      turnId: 't0',
      turnIds: ['t0'],
      additions: 5,
      deletions: 5,
      diffHunks: [],
      status: 'written',
      isNewFile: false,
      originalContent: 'alpha',
      currentContent: 'gamma'
    }
    const inc = computeIncrementalPerItemDiff(
      'EditFile',
      { path: 'src/a.ts', oldText: 'beta', newText: 'gamma' },
      '',
      't1',
      cumulative
    )
    expect(inc?.additions).toBe(1)
    expect(inc?.deletions).toBe(1)
  })

  it('first WriteFile matches extractDiffFromWriteFile', () => {
    const inc = computeIncrementalPerItemDiff(
      'WriteFile',
      { path: 'n.ts', content: 'line\n' },
      '',
      't1',
      undefined
    )
    expect(inc).not.toBeNull()
    expect(inc!.isNewFile).toBe(true)
    expect(inc!.filePath).toBe('n.ts')
  })

  it('second WriteFile diffs old cumulative content against new content', () => {
    const existing = {
      filePath: 'f.ts',
      turnId: 't0',
      turnIds: ['t0'],
      additions: 1,
      deletions: 0,
      diffHunks: [],
      status: 'written' as const,
      isNewFile: true,
      originalContent: '',
      currentContent: 'first'
    }
    const inc = computeIncrementalPerItemDiff(
      'WriteFile',
      { path: 'f.ts', content: 'second' },
      '',
      't1',
      existing
    )
    expect(inc?.currentContent).toBe('second')
    expect(inc?.originalContent).toBe('first')
    expect(inc!.additions + inc!.deletions).toBeGreaterThan(0)
  })
})

describe('computeDiffHunks', () => {
  it('returns empty hunks for identical text', () => {
    const { hunks, additions, deletions } = computeDiffHunks('same\n', 'same\n')
    expect(hunks).toHaveLength(0)
    expect(additions).toBe(0)
    expect(deletions).toBe(0)
  })

  it('counts correct additions/deletions for a simple change', () => {
    const { additions, deletions } = computeDiffHunks('old\n', 'new\n')
    expect(additions).toBe(1)
    expect(deletions).toBe(1)
  })

  it('mergeFileDiffIncrement accumulates WriteFile then EditFile on same path', () => {
    const first = mergeFileDiffIncrement(
      undefined,
      'WriteFile',
      { path: 'src/x.ts', content: 'a\nb\n' },
      '',
      'turn-1'
    )
    expect(first?.currentContent).toBe('a\nb\n')
    const merged = mergeFileDiffIncrement(
      first!,
      'EditFile',
      { path: 'src/x.ts', oldText: 'b', newText: 'c' },
      '',
      'turn-1'
    )
    expect(merged?.currentContent).toBe('a\nc\n')
    expect(merged?.turnIds).toEqual(['turn-1'])
    expect(merged?.additions).toBeGreaterThan(0)
  })

  it('reverseEditReplace restores one occurrence', () => {
    expect(reverseEditReplace('hello world', 'world', 'there')).toBe('hello there')
  })

  it('computeCumulativeFileDiff uses injected readFile for EditFile first edit', async () => {
    const diff = await computeCumulativeFileDiff({
      filePath: 'src/e.ts',
      toolName: 'EditFile',
      args: { path: 'src/e.ts', oldText: 'old', newText: 'new' },
      resultText: 'Successfully edited src/e.ts',
      turnId: 't1',
      existing: undefined,
      workspacePath: 'C:/proj',
      readFile: async () => 'prefix new suffix'
    })
    expect(diff).not.toBeNull()
    expect(diff!.originalContent).toBe('prefix old suffix')
    expect(diff!.currentContent).toBe('prefix new suffix')
    expect(diff!.turnIds).toEqual(['t1'])
  })

  it('readWorkspaceAfterTool retries until newText appears in file (stale first read)', async () => {
    let n = 0
    const readFile = async (): Promise<string> => {
      n++
      if (n === 1) return 'prefix old suffix'
      return 'prefix new suffix'
    }
    const content = await readWorkspaceAfterTool(readFile, 'C:/proj/src/e.ts', 'EditFile', {
      path: 'src/e.ts',
      oldText: 'old',
      newText: 'new'
    })
    expect(content).toBe('prefix new suffix')
    expect(n).toBeGreaterThanOrEqual(2)
  })

  it('computeCumulativeFileDiff falls back to extractDiffFromEditFile when full-file diff is empty', async () => {
    // Disk has no OLD/NEW snippet, so reverseEditReplace cannot change content → full-file diff empty; fallback uses args.
    const diff = await computeCumulativeFileDiff({
      filePath: 'src/e.ts',
      toolName: 'EditFile',
      args: { path: 'src/e.ts', oldText: 'OLD', newText: 'NEW' },
      resultText: 'Successfully edited src/e.ts',
      turnId: 't1',
      existing: undefined,
      workspacePath: 'C:/proj',
      readFile: async () => 'xxxxxxxx'
    })
    expect(diff).not.toBeNull()
    expect(diff!.diffHunks.length).toBeGreaterThan(0)
    expect(diff!.additions + diff!.deletions).toBeGreaterThan(0)
  })

  it('groups nearby changes into a single hunk', () => {
    // Only 2 lines apart — should merge into one hunk
    const old = 'ctx1\nctx2\nchanged1\nctx3\nctx4\nchanged2\nctx5\n'
    const next = 'ctx1\nctx2\nnew1\nctx3\nctx4\nnew2\nctx5\n'
    const { hunks } = computeDiffHunks(old, next)
    expect(hunks).toHaveLength(1)
  })
})

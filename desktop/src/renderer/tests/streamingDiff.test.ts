import { describe, expect, it } from 'vitest'
import { extractDiffFromEditFile } from '../utils/diffExtractor'
import { computeStreamingFileDiff, extractStreamingFilePath } from '../utils/streamingDiff'

describe('streamingDiff', () => {
  it('parses path from partial json arguments', () => {
    expect(extractStreamingFilePath('{"path":"src/foo.ts","content":"abc"}')).toBe('src/foo.ts')
    expect(extractStreamingFilePath('{"content":"abc"}')).toBeNull()
  })

  it('builds WriteFile streaming diff against empty baseline', () => {
    const diff = computeStreamingFileDiff({
      toolName: 'WriteFile',
      turnId: 'turn-1',
      argumentsPreview: '{"path":"src/new.ts","content":"line 1\\nline 2"}',
      filePath: null
    })

    expect(diff).not.toBeNull()
    expect(diff!.filePath).toBe('src/new.ts')
    expect(diff!.isNewFile).toBe(true)
    expect(diff!.deletions).toBe(0)
    expect(diff!.additions).toBe(2)
  })

  it('builds WriteFile streaming diff against disk baseline', () => {
    const diff = computeStreamingFileDiff({
      toolName: 'WriteFile',
      turnId: 'turn-1',
      argumentsPreview: '{"path":"src/app.ts","content":"line1\\nline2\\nline3\\n"}',
      filePath: 'src/app.ts',
      baselineContent: 'line1\nline2\n'
    })

    expect(diff).not.toBeNull()
    expect(diff!.isNewFile).toBe(false)
    expect(diff!.additions).toBe(1)
    expect(diff!.deletions).toBe(0)
    expect(diff!.currentContent).toBe('line1\nline2\nline3\n')
  })

  it('builds EditFile streaming diff from baseline in oldText/newText mode', () => {
    const diff = computeStreamingFileDiff({
      toolName: 'EditFile',
      turnId: 'turn-2',
      argumentsPreview: '{"path":"src/edit.ts","oldText":"old-value","newText":"new"}',
      filePath: 'src/edit.ts',
      baselineContent: 'const value = "old-value"\nconsole.log(value)\n'
    })

    expect(diff).not.toBeNull()
    expect(diff!.additions).toBe(1)
    expect(diff!.deletions).toBe(1)
    expect(diff!.currentContent).toContain('new')
  })

  it('builds EditFile streaming diff from baseline in line-range mode', () => {
    const diff = computeStreamingFileDiff({
      toolName: 'EditFile',
      turnId: 'turn-3',
      argumentsPreview: '{"path":"src/range.ts","startLine":2,"endLine":3,"newText":"X\\nY\\n"}',
      filePath: 'src/range.ts',
      baselineContent: 'A\nB\nC\nD\n'
    })

    expect(diff).not.toBeNull()
    expect(diff!.additions).toBe(2)
    expect(diff!.deletions).toBe(2)
    expect(diff!.currentContent).toBe('A\nX\nY\nD\n')
  })

  it('falls back to oldText/newText diff when baseline is missing', () => {
    const preview = '{"path":"src/fallback.ts","oldText":"before","newText":"after"}'
    const streamingDiff = computeStreamingFileDiff({
      toolName: 'EditFile',
      turnId: 'turn-4',
      argumentsPreview: preview,
      filePath: 'src/fallback.ts'
    })
    const completedDiff = extractDiffFromEditFile(
      { path: 'src/fallback.ts', oldText: 'before', newText: 'after' },
      '',
      'turn-4'
    )

    expect(streamingDiff).not.toBeNull()
    expect(completedDiff).not.toBeNull()
    expect(streamingDiff!.additions).toBe(completedDiff!.additions)
    expect(streamingDiff!.deletions).toBe(completedDiff!.deletions)
    expect(streamingDiff!.diffHunks.length).toBe(completedDiff!.diffHunks.length)
  })
})

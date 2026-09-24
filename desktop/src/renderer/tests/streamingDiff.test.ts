import { describe, expect, it } from 'vitest'
import { computeStreamingFileDiff, extractStreamingFilePath } from '../utils/streamingDiff'

describe('streamingDiff', () => {
  it('parses path from partial json arguments', () => {
    expect(extractStreamingFilePath('{"path":"src/foo.ts","content":"abc"}')).toBe('src/foo.ts')
    expect(extractStreamingFilePath('{"content":"abc"}')).toBeNull()
  })

  it('builds WriteFile streaming diff against empty baseline', () => {
    const diff = computeStreamingFileDiff({
      toolName: 'WriteFile',
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
      argumentsPreview: '{"path":"src/range.ts","startLine":2,"endLine":3,"newText":"X\\nY\\n"}',
      filePath: 'src/range.ts',
      baselineContent: 'A\nB\nC\nD\n'
    })

    expect(diff).not.toBeNull()
    expect(diff!.additions).toBe(2)
    expect(diff!.deletions).toBe(2)
    expect(diff!.currentContent).toBe('A\nX\nY\nD\n')
  })
})

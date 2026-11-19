import { describe, expect, it } from 'vitest'
import { analyzeIndex, removeIndexEntry, upsertIndexEntry } from './memoryIndex'

describe('memory index maintenance', () => {
  it('adds a pointer line for a new entry with the description as its hook', () => {
    expect(upsertIndexEntry('- [Role](user-role.md) — what the user does\n', { file: 'test-style.md', description: 'prefers table tests' }))
      .toBe('- [Role](user-role.md) — what the user does\n- [Test style](test-style.md) — prefers table tests\n')
    expect(upsertIndexEntry('', { file: 'a.md', description: 'hook' })).toBe('- [A](a.md) — hook\n')
  })

  it('renames the pointer with the file and keeps the user-written title and the other lines', () => {
    const index = '# notes\n- [Role](user-role.md) — old hook\n- [Other](other.md) — x\n'
    expect(upsertIndexEntry(index, { previousFile: 'user-role.md', file: 'user-profile.md', previousDescription: 'old hook', description: 'new hook' }))
      .toBe('# notes\n- [Role](user-profile.md) — new hook\n- [Other](other.md) — x\n')
  })

  it('keeps a hook the user wrote instead of the old description', () => {
    expect(upsertIndexEntry('- [Role](role.md) — handwritten\n', { previousFile: 'role.md', file: 'role.md', previousDescription: 'old', description: 'new' }))
      .toBe('- [Role](role.md) — handwritten\n')
  })

  it('removes only the entry line, matching the target case-insensitively when no line matches exactly', () => {
    expect(removeIndexEntry('- [A](a.md) — x\n- [B](./B.md) — y\n', 'b.md')).toBe('- [A](a.md) — x\n')
  })

  it('keeps case-distinct pointers separate when both files exist', () => {
    const index = '- [Upper](Role.md) — x\n- [Lower](role.md) — y\n'
    expect(removeIndexEntry(index, 'role.md')).toBe('- [Upper](Role.md) — x\n')
    expect(upsertIndexEntry(index, { previousFile: 'role.md', file: 'role.md', previousDescription: 'y', description: 'z' }))
      .toBe('- [Upper](Role.md) — x\n- [Lower](role.md) — z\n')
  })

  it('reports the load limits and the lines Core flags', () => {
    const long = `- [Long](long.md) — ${'x'.repeat(200)}`
    const index = ['- [A](a.md) — x', 'free text', long, '- [Gone](gone.md) — y', '', '- [Site](https://example.com) — z'].join('\n')
    const health = analyzeIndex(index, ['a.md', 'long.md', 'orphan.md'])
    expect(health.lines).toBe(6)
    expect(health.nonPointerLines.map((line) => line.lineNumber)).toEqual([2])
    expect(health.longPointerLines.map((line) => line.lineNumber)).toEqual([3])
    expect(health.dangling.map((line) => line.lineNumber)).toEqual([4])
    expect(health.missing).toEqual(['orphan.md'])
    expect(health.overLines || health.overBytes).toBe(false)

    const crowded = analyzeIndex(Array.from({ length: 201 }, (_, i) => `- [N](n${i}.md)`).join('\n'), [])
    expect(crowded.overLines).toBe(true)
    expect(analyzeIndex('é'.repeat(12_501), []).overBytes).toBe(true)
  })
})

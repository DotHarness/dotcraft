import { describe, expect, it } from 'vitest'
import { measureIndex, removeIndexEntry, syncIndex, upsertIndexEntry } from './memoryIndex'

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

  it('links unindexed files and drops pointers to missing files, leaving other lines alone', () => {
    const index = ['# Notes', '- Promo: [A](a.md), [B](b.md)', '- [Gone](gone.md) — y', '- [Site](https://example.com) — z', '- [Nested](sub/x.md)'].join('\n')
    const descriptions = new Map([['new-one.md', 'fresh']])
    expect(syncIndex(index, ['a.md', 'b.md', 'new-one.md'], descriptions)).toBe([
      '# Notes', '- Promo: [A](a.md), [B](b.md)', '- [Site](https://example.com) — z', '- [Nested](sub/x.md)', '- [New one](new-one.md) — fresh'
    ].join('\n') + '\n')
    expect(syncIndex(null, ['a.md'], new Map([['a.md', 'x']]))).toBe('- [A](a.md) — x\n')
  })

  it('reports no change when every file is linked and every pointer resolves', () => {
    expect(syncIndex('- [A](./A.md) — x\r\n', ['a.md'], new Map())).toBeNull()
    expect(syncIndex('- [Notes](my%20notes.md) — x\n', ['my notes.md'], new Map())).toBeNull()
    expect(syncIndex(null, [], new Map())).toBeNull()
  })

  it('skips files a pointer cannot name and clips long hooks to the pointer limit', () => {
    expect(syncIndex(null, ['my notes.md', 'a(1).md'], new Map())).toBeNull()
    const synced = syncIndex(null, ['long.md'], new Map([['long.md', 'x'.repeat(400)]]))!
    expect(synced.trimEnd().length).toBeLessThanOrEqual(200)
    expect(synced.trimEnd().endsWith('…')).toBe(true)
    expect(syncIndex(synced, ['long.md'], new Map())).toBeNull()
  })

  it('measures the index against the load limits as Core does', () => {
    expect(measureIndex('- [A](a.md)\n\n- [B](b.md)\n')).toEqual({ lines: 3, bytes: 24, load: 'ok' })
    expect(measureIndex(Array.from({ length: 161 }, (_, i) => `- [N](n${i}.md)`).join('\n')).load).toBe('near')
    expect(measureIndex(Array.from({ length: 201 }, (_, i) => `- [N](n${i}.md)`).join('\n')).load).toBe('over')
    expect(measureIndex('é'.repeat(12_501)).load).toBe('over')
  })
})

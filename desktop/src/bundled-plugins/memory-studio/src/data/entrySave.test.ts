import { describe, expect, it } from 'vitest'
import { deleteEntry, readSnapshot, saveEntry } from './entrySave'
import { parseMemoryFile } from './memoryFile'
import type { MemoryFs } from './memoryFs'

const STORE = 'C:\\ws\\.craft\\memory'

function fakeFs(files: Record<string, string>): MemoryFs & { files: Map<string, string> } {
  const map = new Map(Object.entries(files).map(([name, text]) => [`${STORE}\\${name}`, text]))
  return {
    files: map,
    readText: async (path) => map.get(path) ?? null,
    writeText: async (path, text) => { map.set(path, text) },
    remove: async (path) => { map.delete(path) },
    listFiles: async () => [...map.keys()].map((path) => path.slice(STORE.length + 1)),
    modifiedAt: async () => 0,
    ensureDirectory: async () => undefined
  }
}

const file = (name: string, description: string, body: string): string =>
  `---\nname: ${name}\ndescription: ${description}\nmetadata:\n  type: user\n---\n\n${body}\n`

describe('memory entry saves', () => {
  it('writes a new entry and its index line', async () => {
    const fs = fakeFs({ 'MEMORY.md': '- [A](a.md) — x\n' })
    const snapshot = await readSnapshot(fs, STORE, null)
    const outcome = await saveEntry(fs, snapshot, { name: 'test-style', description: 'prefers table tests', type: 'feedback', body: 'Use table tests.' })

    expect(outcome.kind).toBe('saved')
    expect(parseMemoryFile(fs.files.get(`${STORE}\\test-style.md`)!)).toMatchObject({ name: 'test-style', description: 'prefers table tests', type: 'feedback', body: 'Use table tests.\n' })
    expect(fs.files.get(`${STORE}\\MEMORY.md`)).toBe('- [A](a.md) — x\n- [Test style](test-style.md) — prefers table tests\n')
  })

  it('renames the file and its index line together', async () => {
    const fs = fakeFs({ 'role.md': file('role', 'old', 'Body'), 'MEMORY.md': '- [Role](role.md) — old\n' })
    const snapshot = await readSnapshot(fs, STORE, 'role.md')
    await saveEntry(fs, snapshot, { name: 'user-role', description: 'new', type: 'user', body: 'Body' })

    expect(fs.files.has(`${STORE}\\role.md`)).toBe(false)
    expect(fs.files.has(`${STORE}\\user-role.md`)).toBe(true)
    expect(fs.files.get(`${STORE}\\MEMORY.md`)).toBe('- [Role](user-role.md) — new\n')
  })

  it('refuses to save over a file or index changed since it was read, leaving both untouched', async () => {
    const fs = fakeFs({ 'role.md': file('role', 'old', 'Body'), 'MEMORY.md': '- [Role](role.md) — old\n' })
    const snapshot = await readSnapshot(fs, STORE, 'role.md')
    fs.files.set(`${STORE}\\role.md`, file('role', 'old', 'Agent edit'))

    const fileConflict = await saveEntry(fs, snapshot, { name: 'role', description: 'mine', type: 'user', body: 'Mine' })
    expect(fileConflict).toMatchObject({ kind: 'conflict', reason: 'fileChanged' })
    expect(fs.files.get(`${STORE}\\role.md`)).toContain('Agent edit')

    const fresh = await readSnapshot(fs, STORE, 'role.md')
    fs.files.set(`${STORE}\\MEMORY.md`, '- [Role](role.md) — old\n- [B](b.md) — new\n')
    const indexConflict = await deleteEntry(fs, fresh)
    expect(indexConflict).toMatchObject({ kind: 'conflict', reason: 'indexChanged' })
    expect(fs.files.has(`${STORE}\\role.md`)).toBe(true)
  })

  it('refuses a new name another memory already uses', async () => {
    const fs = fakeFs({ 'role.md': file('role', 'x', 'Existing'), 'MEMORY.md': '' })
    const outcome = await saveEntry(fs, await readSnapshot(fs, STORE, null), { name: 'role', description: 'y', type: 'user', body: 'New' })
    expect(outcome).toMatchObject({ kind: 'conflict', reason: 'nameTaken' })
    expect(fs.files.get(`${STORE}\\role.md`)).toContain('Existing')
  })

  it('deletes the file and its index line', async () => {
    const fs = fakeFs({ 'role.md': file('role', 'x', 'Body'), 'MEMORY.md': '- [Role](role.md) — x\n- [B](b.md) — y\n' })
    expect(await deleteEntry(fs, await readSnapshot(fs, STORE, 'role.md'))).toEqual({ kind: 'deleted' })
    expect(fs.files.has(`${STORE}\\role.md`)).toBe(false)
    expect(fs.files.get(`${STORE}\\MEMORY.md`)).toBe('- [B](b.md) — y\n')
  })
})

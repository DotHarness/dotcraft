import { describe, expect, it } from 'vitest'
import type { MemoryFs } from './memoryFs'
import { clearStore, loadStore, type MemoryStore } from './stores'

const STORE = 'C:\\ws\\.craft\\memory'

function fakeFs(files: Record<string, string>, onRead?: (path: string, map: Map<string, string>) => void): MemoryFs & { files: Map<string, string> } {
  const map = new Map(Object.entries(files).map(([name, text]) => [`${STORE}\\${name}`, text]))
  return {
    files: map,
    readText: async (path) => {
      onRead?.(path, map)
      return map.get(path) ?? null
    },
    writeText: async (path, text) => { map.set(path, text) },
    remove: async (path) => { map.delete(path) },
    listFiles: async () => [...map.keys()].map((path) => path.slice(STORE.length + 1)),
    modifiedAt: async () => 0,
    ensureDirectory: async () => undefined
  }
}

const store = (writable: boolean): MemoryStore => ({ key: 'workspace', kind: 'workspace', scope: null, id: null, path: STORE, displayName: '', writable })
const memory = '---\nname: role\ndescription: what the user does\nmetadata:\n  type: user\n---\n\nBody\n'

describe('loading a store', () => {
  it('writes the synced index for a writable store', async () => {
    const fs = fakeFs({ 'role.md': memory, 'MEMORY.md': '- [Gone](gone.md) — x\n' })
    const contents = await loadStore(fs, store(true))
    expect(fs.files.get(`${STORE}\\MEMORY.md`)).toBe('- [Role](role.md) — what the user does\n')
    expect(contents.indexText).toBe('- [Role](role.md) — what the user does\n')
    expect(contents.usage.lines).toBe(1)
  })

  it('leaves a read-only store untouched', async () => {
    const fs = fakeFs({ 'role.md': memory })
    await loadStore(fs, store(false))
    expect(fs.files.has(`${STORE}\\MEMORY.md`)).toBe(false)
  })

  it('keeps pointers to files that fail to read', async () => {
    const fs = fakeFs({ 'role.md': memory, 'MEMORY.md': '- [Role](role.md) — mine\n' })
    fs.readText = async (path) => path.endsWith('role.md') ? null : fs.files.get(path) ?? null
    const contents = await loadStore(fs, store(true))
    expect(fs.files.get(`${STORE}\\MEMORY.md`)).toBe('- [Role](role.md) — mine\n')
    expect(contents.entries).toEqual([])
  })

  it('skips the sync when the store directory is not listed', async () => {
    const fs = fakeFs({ 'MEMORY.md': '- [Role](role.md) — mine\n' })
    fs.listFiles = async () => null
    await loadStore(fs, store(true))
    expect(fs.files.get(`${STORE}\\MEMORY.md`)).toBe('- [Role](role.md) — mine\n')
  })

  it('skips the write when the index changed after it was read', async () => {
    let reads = 0
    const fs = fakeFs({ 'role.md': memory, 'MEMORY.md': '' }, (path, map) => {
      if (path.endsWith('MEMORY.md') && ++reads === 2) map.set(path, '- [Other](other.md)\n')
    })
    const contents = await loadStore(fs, store(true))
    expect(fs.files.get(`${STORE}\\MEMORY.md`)).toBe('- [Other](other.md)\n')
    expect(contents.indexText).toBe('- [Other](other.md)\n')
  })
})

describe('clearing a store', () => {
  it('removes every memory file and the index but nothing else', async () => {
    const fs = fakeFs({ 'role.md': memory, 'MEMORY.md': '- [Role](role.md) — x\n', 'notes.txt': 'keep' })
    await clearStore(fs, store(true))
    expect([...fs.files.keys()]).toEqual([`${STORE}\\notes.txt`])
  })
})

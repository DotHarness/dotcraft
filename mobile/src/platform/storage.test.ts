/// <reference types="node" />
import { afterEach, describe, expect, it, vi } from 'vitest'
import { mkdtemp, readFile, rename, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import type { PersistedState } from '../core/state'

const directory = vi.hoisted(() => ({ path: '', failWrite: false }))

vi.mock('expo-file-system/legacy', () => ({
  get documentDirectory() { return `${directory.path}/` },
  readAsStringAsync: (path: string) => readFile(path, 'utf8'),
  writeAsStringAsync: async (path: string, content: string) => {
    if (directory.failWrite) {
      await writeFile(path, 'incomplete')
      throw new Error('interrupted write')
    }
    await writeFile(path, content)
  },
  moveAsync: ({ from, to }: { from: string; to: string }) => rename(from, to),
  deleteAsync: (path: string, options: { idempotent: boolean }) => rm(path, { force: options.idempotent }),
}))

async function storage() {
  directory.path = await mkdtemp(join(tmpdir(), 'dotcraft-persistence-'))
  directory.failWrite = false
  vi.resetModules()
  return (await import('./storage')).stateStorage
}

afterEach(async () => {
  if (directory.path) await rm(directory.path, { force: true, recursive: true })
})

const snapshot: PersistedState = { order: [], selected: null, computers: {} }

describe('snapshot files', () => {
  it('loads the replacement snapshot and removes it when cleared', async () => {
    const files = await storage()
    expect(await files.load()).toBeNull()
    await files.save(snapshot)
    await files.save({ ...snapshot, selected: 'new-selection' })
    expect(await files.load()).toEqual({ ...snapshot, selected: 'new-selection' })
    await files.clear()
    expect(await files.load()).toBeNull()
  })

  it('retains the previous snapshot if writing the temporary file is interrupted', async () => {
    const files = await storage()
    await files.save(snapshot)
    directory.failWrite = true
    await expect(files.save({ ...snapshot, selected: 'unfinished' })).rejects.toThrow('interrupted write')
    expect(await files.load()).toEqual(snapshot)
    await files.clear()
    expect(await files.load()).toBeNull()
  })
})

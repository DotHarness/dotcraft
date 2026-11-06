import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { BrowserPageSnapshotStore, type BrowserPageSnapshotData } from '../browserPageSnapshotStore'

let directory: string
let path: string
beforeEach(() => { directory = mkdtempSync(join(tmpdir(), 'dotcraft-page-state-')); path = join(directory, 'pages.json') })
afterEach(() => { rmSync(directory, { recursive: true, force: true }); vi.restoreAllMocks() })

function snapshot(id = 'browser:one', updatedAt = 1): BrowserPageSnapshotData {
  return {
    browserStorageId: id, owner: { projectKey: 'project:a', threadId: 'thread:a', tabId: id }, workspacePath: '/workspace/a',
    kind: 'website', url: 'https://example.com/second', title: 'Second', updatedAt,
    navigation: { index: 1, entries: [{ url: 'https://example.com/', title: 'First', pageState: 'cGFnZQ==' }, { url: 'https://example.com/second', title: 'Second' }] }
  }
}

describe('browser page snapshot storage', () => {
  it('atomically reconstructs history and Chromium page state without sharing mutable records', () => {
    const first = snapshot()
    const store = new BrowserPageSnapshotStore(path)
    expect(store.set(first)).toBe(true)
    first.navigation.entries[0].title = 'Changed after save'
    const restored = new BrowserPageSnapshotStore(path).get(first.browserStorageId, first.owner)!
    expect(restored.navigation).toEqual({ index: 1, entries: [{ url: 'https://example.com/', title: 'First', pageState: 'cGFnZQ==' }, { url: 'https://example.com/second', title: 'Second' }] })
    restored.navigation.entries.length = 0
    expect(store.get(first.browserStorageId, first.owner)?.navigation.entries).toHaveLength(2)
  })

  it('refuses to read, overwrite or delete another project/thread/tab owner', () => {
    const original = snapshot()
    const store = new BrowserPageSnapshotStore(path)
    store.set(original)
    for (const owner of [
      { ...original.owner, projectKey: 'project:b' }, { ...original.owner, threadId: 'thread:b' }, { ...original.owner, tabId: 'tab:b' }
    ]) {
      expect(store.get(original.browserStorageId, owner)).toBeUndefined()
      expect(store.set({ ...original, owner, title: 'Hijacked' })).toBe(false)
      store.delete(original.browserStorageId, owner)
    }
    expect(store.get(original.browserStorageId, original.owner)?.title).toBe('Second')
  })

  it('moves a Welcome page owner rather than keeping a duplicate snapshot', () => {
    const value = snapshot()
    const store = new BrowserPageSnapshotStore(path)
    store.set(value)
    const next = { ...value.owner, threadId: 'created-thread' }
    store.rebind(value.browserStorageId, value.owner, next)
    expect(store.get(value.browserStorageId, value.owner)).toBeUndefined()
    expect(new BrowserPageSnapshotStore(path).get(value.browserStorageId, next)?.navigation.index).toBe(1)
    store.delete(value.browserStorageId, next)
    expect(new BrowserPageSnapshotStore(path).owner(value.browserStorageId)).toBeUndefined()
  })

  it('evicts oldest snapshots by count and bytes without retaining oversized page states', () => {
    const store = new BrowserPageSnapshotStore(path, { maxPages: 2, maxBytes: 4000 })
    store.set(snapshot('first', 1)); store.set(snapshot('second', 2)); store.set(snapshot('third', 3))
    expect(store.owner('first')).toBeUndefined()
    expect(store.owner('second')).toBeDefined()
    const huge = snapshot('huge', 4)
    huge.navigation.entries[0].pageState = 'x'.repeat(5000)
    expect(store.set(huge)).toBe(false)
    expect(Buffer.byteLength(readFileSync(path, 'utf8'))).toBeLessThanOrEqual(4000)
    expect(new BrowserPageSnapshotStore(path).owner('huge')).toBeUndefined()
  })

  it.each(['{', JSON.stringify({ version: 7, pages: { 'browser:one': snapshot() } })])('ignores damaged or unsupported data', data => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    writeFileSync(path, data)
    const store = new BrowserPageSnapshotStore(path)
    expect(store.owner('browser:one')).toBeUndefined()
    expect(store.set(snapshot())).toBe(true)
  })

  it('does not report success when an atomic write cannot complete', () => {
    vi.spyOn(console, 'warn').mockImplementation(() => {})
    writeFileSync(path, 'not a directory')
    expect(new BrowserPageSnapshotStore(join(path, 'pages.json')).set(snapshot())).toBe(false)
  })
})

import type { MemoryStoreInfo } from '@dotcraft/sdk/contracts'
import { joinHostPath } from './hostPath'
import { parseMemoryFile } from './memoryFile'
import type { MemoryFs } from './memoryFs'
import { saveIndex } from './entrySave'
import { INDEX_FILE, measureIndex, syncIndex, type IndexUsage } from './memoryIndex'
import type { Rpc } from './rpc'

export interface MemoryStore {
  key: string
  kind: 'workspace' | 'scope' | 'attached'
  scope: string | null
  id: string | null
  path: string
  displayName: string
  writable: boolean
}

export interface MemoryEntry {
  storeKey: string
  fileName: string
  name: string
  description: string
  /** `metadata.type` as written, which may be a type the policy does not declare. */
  type: string
  body: string
  modifiedAtMs: number
}

export interface StoreContents {
  store: MemoryStore
  entries: MemoryEntry[]
  indexText: string | null
  usage: IndexUsage
}

export const STALE_AFTER_MS = 30 * 24 * 60 * 60 * 1000

export function isStale(entry: Pick<MemoryEntry, 'modifiedAtMs'>, now: number): boolean {
  return entry.modifiedAtMs > 0 && now - entry.modifiedAtMs > STALE_AFTER_MS
}

function toStore(info: MemoryStoreInfo): MemoryStore {
  const kind = info.kind === 'scope' || info.kind === 'attached' ? info.kind : 'workspace'
  const scope = info.scope ?? null
  const id = info.id ?? null
  return {
    key: kind === 'workspace' ? 'workspace' : `${kind}:${scope ?? id ?? ''}`,
    kind,
    scope,
    id,
    path: info.path ?? '',
    displayName: info.displayName ?? scope ?? id ?? '',
    writable: info.writable === true
  }
}

export async function listStores(rpc: Rpc): Promise<MemoryStore[]> {
  const { stores = [] } = await rpc.request('memory/listStores', {})
  return stores.map(toStore)
}

export function isMemoryFile(fileName: string): boolean {
  return /\.md$/i.test(fileName) && fileName.toLowerCase() !== INDEX_FILE.toLowerCase()
}

export async function loadStore(fs: MemoryFs, store: MemoryStore): Promise<StoreContents> {
  const listed = await fs.listFiles(store.path)
  const names = (listed ?? []).filter(isMemoryFile)
  const [indexText, entries] = await Promise.all([
    fs.readText(joinHostPath(store.path, INDEX_FILE)),
    Promise.all(names.map(async (fileName): Promise<MemoryEntry | null> => {
      const path = joinHostPath(store.path, fileName)
      const [text, modifiedAtMs] = await Promise.all([fs.readText(path), fs.modifiedAt(path).catch(() => 0)])
      if (text == null) return null
      const parsed = parseMemoryFile(text)
      return {
        storeKey: store.key,
        fileName,
        name: parsed.name || fileName.replace(/\.md$/i, ''),
        description: parsed.description,
        type: parsed.type,
        body: parsed.body,
        modifiedAtMs
      }
    }))
  ])
  const present = entries.filter((entry): entry is MemoryEntry => entry != null)
    .sort((a, b) => b.modifiedAtMs - a.modifiedAtMs || a.fileName.localeCompare(b.fileName))
  const synced = store.writable && listed != null ? await syncStoreIndex(fs, store, indexText, names, present) : indexText
  return { store, entries: present, indexText: synced, usage: measureIndex(synced) }
}

async function syncStoreIndex(fs: MemoryFs, store: MemoryStore, indexText: string | null, files: readonly string[], entries: readonly MemoryEntry[]): Promise<string | null> {
  const next = syncIndex(indexText, files, new Map(entries.map((entry) => [entry.fileName, entry.description])))
  if (next == null) return indexText
  try {
    const outcome = await saveIndex(fs, store.path, indexText, next)
    return outcome.kind === 'saved' ? next : outcome.current
  } catch {
    return indexText
  }
}

export function matchesSearch(entry: MemoryEntry, query: string): boolean {
  const needle = query.trim().toLowerCase()
  if (!needle) return true
  return [entry.name, entry.fileName, entry.description, entry.body].some((field) => field.toLowerCase().includes(needle))
}

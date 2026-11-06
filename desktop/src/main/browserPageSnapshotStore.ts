import { existsSync, mkdirSync, readFileSync, renameSync, statSync, writeFileSync } from 'node:fs'
import { dirname } from 'node:path'

export interface BrowserPageOwner {
  projectKey: string
  threadId?: string
  tabId: string
}
export interface BrowserPageSnapshotData {
  browserStorageId: string
  owner: BrowserPageOwner
  workspacePath: string
  url: string
  title: string
  faviconDataUrl?: string
  kind: 'website' | 'local-preview'
  navigation: { entries: Electron.NavigationEntry[]; index: number }
  updatedAt: number
}

export function sameBrowserPageOwner(a: BrowserPageOwner, b: BrowserPageOwner): boolean {
  return a.projectKey === b.projectKey && a.threadId === b.threadId && a.tabId === b.tabId
}

function validSnapshot(value: unknown): value is BrowserPageSnapshotData {
  if (!value || typeof value !== 'object') return false
  const data = value as BrowserPageSnapshotData
  return typeof data.browserStorageId === 'string' && data.browserStorageId.length > 0 &&
    typeof data.owner?.projectKey === 'string' && typeof data.owner.tabId === 'string' &&
    (data.owner.threadId === undefined || typeof data.owner.threadId === 'string') &&
    typeof data.workspacePath === 'string' && typeof data.url === 'string' && typeof data.title === 'string' &&
    (data.faviconDataUrl === undefined || typeof data.faviconDataUrl === 'string') &&
    (data.kind === 'website' || data.kind === 'local-preview') && Number.isFinite(data.updatedAt) &&
    Array.isArray(data.navigation?.entries) && data.navigation.entries.length > 0 &&
    Number.isInteger(data.navigation.index) && data.navigation.index >= 0 && data.navigation.index < data.navigation.entries.length &&
    data.navigation.entries.every(entry => entry && typeof entry.url === 'string' && typeof entry.title === 'string' &&
      (entry.pageState === undefined || typeof entry.pageState === 'string'))
}

export class BrowserPageSnapshotStore {
  private readonly pages = new Map<string, BrowserPageSnapshotData>()
  private readonly maxPages: number
  private readonly maxBytes: number

  constructor(private readonly filePath?: string, limits: { maxPages?: number; maxBytes?: number } = {}) {
    this.maxPages = limits.maxPages ?? 100
    this.maxBytes = limits.maxBytes ?? 64 * 1024 * 1024
    if (!filePath || !existsSync(filePath)) return
    try {
      if (statSync(filePath).size > this.maxBytes) return
      const data: unknown = JSON.parse(readFileSync(filePath, 'utf8'))
      if (!data || typeof data !== 'object' || (data as { version?: unknown }).version !== 1) return
      const pages = (data as { pages?: unknown }).pages
      if (!pages || typeof pages !== 'object' || Array.isArray(pages)) return
      for (const [id, page] of Object.entries(pages)) {
        if (validSnapshot(page) && page.browserStorageId === id) this.pages.set(id, page)
      }
      this.trim()
    } catch {
      console.warn('Browser page snapshots could not be read.')
    }
  }

  get(id: string, owner: BrowserPageOwner): BrowserPageSnapshotData | undefined {
    const snapshot = this.pages.get(id)
    return snapshot && sameBrowserPageOwner(snapshot.owner, owner) ? structuredClone(snapshot) : undefined
  }

  owner(id: string): BrowserPageOwner | undefined {
    const owner = this.pages.get(id)?.owner
    return owner ? { ...owner } : undefined
  }

  set(snapshot: BrowserPageSnapshotData): boolean {
    if (Buffer.byteLength(JSON.stringify({ version: 1, pages: { [snapshot.browserStorageId]: snapshot } }), 'utf8') > this.maxBytes) return false
    const owner = this.owner(snapshot.browserStorageId)
    if (owner && !sameBrowserPageOwner(owner, snapshot.owner)) return false
    this.pages.set(snapshot.browserStorageId, structuredClone(snapshot))
    this.trim()
    const retained = this.pages.has(snapshot.browserStorageId)
    return this.flush() && retained
  }

  delete(id: string, owner: BrowserPageOwner): void {
    const existing = this.pages.get(id)
    if (!existing || !sameBrowserPageOwner(existing.owner, owner)) return
    this.pages.delete(id)
    this.flush()
  }

  deleteThread(projectKey: string, threadId: string): void {
    let changed = false
    for (const [id, page] of this.pages) {
      if (page.owner.projectKey === projectKey && page.owner.threadId === threadId) {
        this.pages.delete(id)
        changed = true
      }
    }
    if (changed) this.flush()
  }

  rebind(id: string, from: BrowserPageOwner, to: BrowserPageOwner): void {
    const snapshot = this.pages.get(id)
    if (!snapshot || !sameBrowserPageOwner(snapshot.owner, from)) return
    snapshot.owner = { ...to }
    this.flush()
  }

  private contents(): string {
    return JSON.stringify({ version: 1, pages: Object.fromEntries(this.pages) })
  }

  private trim(): void {
    const oldest = [...this.pages.values()].sort((a, b) => a.updatedAt - b.updatedAt)
    for (const page of oldest) {
      if (this.pages.size <= this.maxPages && Buffer.byteLength(this.contents(), 'utf8') <= this.maxBytes) break
      this.pages.delete(page.browserStorageId)
    }
  }

  private flush(): boolean {
    if (!this.filePath) return true
    try {
      mkdirSync(dirname(this.filePath), { recursive: true })
      const temporary = `${this.filePath}.tmp`
      writeFileSync(temporary, this.contents(), 'utf8')
      renameSync(temporary, this.filePath)
      return true
    } catch {
      console.warn('Browser page snapshots could not be saved.')
      return false
    }
  }
}

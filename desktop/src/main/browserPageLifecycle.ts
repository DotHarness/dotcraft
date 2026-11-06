import { randomUUID } from 'node:crypto'
import type { BrowserWindow, WebContents } from 'electron'
import { browserPageAuthority, browserSessionKind } from './browserSessionPolicy'
import { normalizeWorkspaceProjectKey } from '../shared/workspaceProjectKey'
import { BrowserPageSnapshotStore, sameBrowserPageOwner, type BrowserPageOwner, type BrowserPageSnapshotData } from './browserPageSnapshotStore'
import type { BrowserRestoreResult } from '../shared/viewer/types'

export interface BrowserPageParams {
  tabId: string
  threadId?: string
  workspacePath: string
  projectKey?: string
  browserStorageId?: string
  initialUrl?: string
  sessionUrl?: string
  allowFileScheme?: boolean
  skipStartPageLoad?: boolean
  automation?: boolean
}
export interface BrowserPageEntry {
  win: BrowserWindow
  owner: BrowserPageOwner
  params: BrowserPageParams
  browserStorageId: string
  page?: WebContents
  snapshot?: BrowserPageSnapshotData
  faviconDataUrl?: string
  restoring: boolean
  visible: boolean
  automation: boolean
  capture: boolean
  selectedAt?: number
}
export type BrowserRestoreValidator = (win: BrowserWindow, url: string, context: BrowserPageOwner & { workspacePath: string }) => Promise<void>

export class BrowserPageLifecycle {
  private readonly windows = new Map<number, Map<string, BrowserPageEntry>>()
  private readonly selected = new Map<string, BrowserPageEntry>()
  private readonly foreground = new Map<number, { projectKey: string; threadId?: string }>()
  private readonly timers = new Map<number, ReturnType<typeof setTimeout>>()
  private readonly closedStorageIds = new Set<string>()
  private initialOwner?: number
  private validator?: BrowserRestoreValidator
  private enforcing = false

  constructor(
    private readonly store: () => BrowserPageSnapshotStore,
    private readonly suspend: (entry: BrowserPageEntry) => void,
    private readonly hasDownload: (tabId: string) => boolean,
    private readonly now: () => number = Date.now,
    private readonly budget = 32
  ) {}

  registerPersistenceOwner(win: BrowserWindow): void { this.initialOwner ??= win.id }
  setRestoreValidator(validator: BrowserRestoreValidator): void { this.validator = validator }
  get(win: BrowserWindow, tabId: string): BrowserPageEntry | undefined { return this.windows.get(win.id)?.get(tabId) }
  entries(win: BrowserWindow): BrowserPageEntry[] { return [...this.windows.get(win.id)?.values() ?? []] }
  current(entry: BrowserPageEntry): boolean { return this.get(entry.win, entry.owner.tabId) === entry && !entry.win.isDestroyed() }

  prepare(win: BrowserWindow, params: BrowserPageParams): BrowserPageEntry {
    const owner: BrowserPageOwner = { projectKey: params.projectKey ?? normalizeWorkspaceProjectKey(params.workspacePath), threadId: params.threadId, tabId: params.tabId }
    const existing = this.get(win, params.tabId)
    if (existing) {
      if (!sameBrowserPageOwner(existing.owner, owner) || (params.browserStorageId && params.browserStorageId !== existing.browserStorageId)) throw new Error('Browser tab owner does not match.')
      return existing
    }
    const id = params.browserStorageId ?? `browser:${randomUUID()}`
    if (this.closedStorageIds.has(id)) throw new Error('Browser page closed.')
    for (const tabs of this.windows.values()) {
      for (const entry of tabs.values()) {
        if (entry.browserStorageId === id) throw new Error('Browser page is owned by another window or tab.')
      }
    }
    let snapshot: BrowserPageSnapshotData | undefined
    if (params.browserStorageId && this.initialOwner !== undefined) {
      if (win.id !== this.initialOwner) throw new Error('Browser page is owned by another window or tab.')
      const savedOwner = this.store().owner(id)
      if (savedOwner && !sameBrowserPageOwner(savedOwner, owner)) {
        throw new Error('Browser page is owned by another window or tab.')
      }
      snapshot = this.store().get(id, owner)
    }
    const entry: BrowserPageEntry = {
      win, owner, params: { ...params, projectKey: owner.projectKey, browserStorageId: id }, browserStorageId: id,
      snapshot, restoring: false, visible: false, automation: params.automation === true, capture: false
    }
    let tabs = this.windows.get(win.id)
    if (!tabs) this.windows.set(win.id, tabs = new Map())
    tabs.set(params.tabId, entry)
    return entry
  }

  restoreState(win: BrowserWindow, params: Omit<BrowserPageParams, 'tabId'> & { tabs: Array<{ tabId: string; browserStorageId: string; initialUrl?: string }> }): BrowserRestoreResult[] {
    return params.tabs.map(tab => {
      const owner: BrowserPageOwner = { projectKey: params.projectKey ?? normalizeWorkspaceProjectKey(params.workspacePath), threadId: params.threadId, tabId: tab.tabId }
      if (this.closedStorageIds.has(tab.browserStorageId)) return { ...tab, status: 'missing' }
      const local = this.get(win, tab.tabId)
      if (local && local.browserStorageId === tab.browserStorageId && sameBrowserPageOwner(local.owner, owner)) {
        return { tabId: tab.tabId, browserStorageId: tab.browserStorageId, status: local.page ? 'already-live' : 'snapshot-ready', suspended: !local.page }
      }
      if (this.initialOwner !== win.id) return { ...tab, status: 'owned-by-another-window' }
      for (const tabs of this.windows.values()) {
        if ([...tabs.values()].some(entry => entry.browserStorageId === tab.browserStorageId)) return { ...tab, status: 'owned-by-another-window' }
      }
      const savedOwner = this.store().owner(tab.browserStorageId)
      if (savedOwner && !sameBrowserPageOwner(savedOwner, owner)) return { ...tab, status: 'owned-by-another-window' }
      const snapshot = this.store().get(tab.browserStorageId, owner)
      if (!snapshot) {
        this.prepare(win, { ...params, ...tab })
        return { ...tab, status: 'missing' }
      }
      this.prepare(win, { ...params, ...tab, initialUrl: snapshot.url })
      return { ...tab, status: 'snapshot-ready', suspended: true, currentUrl: snapshot.url, title: snapshot.title, faviconDataUrl: snapshot.faviconDataUrl,
        canGoBack: snapshot.navigation.index > 0, canGoForward: snapshot.navigation.index < snapshot.navigation.entries.length - 1 }
    })
  }

  async restore(entry: BrowserPageEntry, page: WebContents): Promise<boolean> {
    const snapshot = entry.snapshot
    if (!snapshot) return false
    const kind = browserPageAuthority(page).kind
    if (snapshot.kind !== kind) throw new Error('Browser snapshot profile does not match this page.')
    for (const item of snapshot.navigation.entries) {
      await this.validateNavigation(entry, item.url, kind)
      if (!this.current(entry) || page.isDestroyed()) throw new Error('Browser page closed.')
    }
    try {
      await page.navigationHistory.restore(snapshot.navigation)
    } catch {
      if (!this.current(entry) || page.isDestroyed()) throw new Error('Browser page closed.')
      console.warn('Browser navigation history could not be restored.')
      return false
    }
    if (!this.current(entry) || page.isDestroyed()) throw new Error('Browser page closed.')
    return true
  }

  async validateNavigation(entry: BrowserPageEntry, url: string, kind: 'website' | 'local-preview'): Promise<void> {
    if (url === 'about:blank') return
    const protocol = new URL(url).protocol
    if (!['http:', 'https:', 'dotcraft-viewer:', 'file:'].includes(protocol) || browserSessionKind(url) !== kind) {
      throw new Error('Browser snapshot contains an unsupported navigation.')
    }
    if (kind === 'local-preview') {
      if (!this.validator) throw new Error('Local preview snapshot requires file authorization.')
      await this.validator(entry.win, url, { ...entry.owner, workspacePath: entry.params.workspacePath })
    }
  }

  checkpoint(entry: BrowserPageEntry): boolean {
    const page = entry.page
    if (this.initialOwner === undefined || !page || page.isDestroyed() || entry.restoring) return false
    const all = page.navigationHistory.getAllEntries()
    const activeIndex = page.navigationHistory.getActiveIndex()
    const allowed = (url: string): boolean => {
      try { return url === 'about:blank' || ['http:', 'https:', 'file:', 'dotcraft-viewer:'].includes(new URL(url).protocol) } catch { return false }
    }
    if (!all[activeIndex] || !allowed(all[activeIndex].url)) return false
    const entries = all.filter(item => allowed(item.url))
    const actualIndex = all.slice(0, activeIndex).filter(item => allowed(item.url)).length
    const snapshot: BrowserPageSnapshotData = {
      browserStorageId: entry.browserStorageId, owner: { ...entry.owner }, workspacePath: entry.params.workspacePath,
      url: page.getURL(), title: page.getTitle(), kind: browserPageAuthority(page).kind,
      navigation: { entries, index: actualIndex }, updatedAt: this.now(), faviconDataUrl: entry.faviconDataUrl ?? entry.snapshot?.faviconDataUrl
    }
    const saved = this.initialOwner !== entry.win.id || this.store().set(snapshot)
    if (saved) entry.snapshot = snapshot
    return saved
  }

  close(win: BrowserWindow, tabId: string, explicit: boolean): void {
    const entry = this.get(win, tabId)
    if (!entry) return
    if (explicit) this.closedStorageIds.add(entry.browserStorageId)
    if (explicit && this.initialOwner === win.id) this.store().delete(entry.browserStorageId, entry.owner)
    else if (!explicit) this.checkpoint(entry)
    this.windows.get(win.id)?.delete(tabId)
    for (const [key, selected] of this.selected) if (selected === entry) this.selected.delete(key)
  }

  forgetThread(win: BrowserWindow, projectKey: string, threadId: string): void {
    if (this.initialOwner === win.id) this.store().deleteThread(projectKey, threadId)
  }

  disposeWindow(win: BrowserWindow): void {
    for (const entry of this.entries(win)) this.close(win, entry.owner.tabId, false)
    this.windows.delete(win.id)
    this.foreground.delete(win.id)
    const timer = this.timers.get(win.id)
    if (timer) clearTimeout(timer)
    this.timers.delete(win.id)
  }

  rebindThread(win: BrowserWindow, from: string, to: string): void {
    for (const entry of this.entries(win)) {
      if (entry.owner.threadId !== from) continue
      const previous = { ...entry.owner }
      entry.owner.threadId = to
      entry.params.threadId = to
      if (this.initialOwner === win.id) this.store().rebind(entry.browserStorageId, previous, entry.owner)
    }
  }

  setForeground(win: BrowserWindow, scope: { projectKey: string; threadId?: string }): void {
    this.foreground.set(win.id, scope)
    this.enforce(win)
  }

  select(win: BrowserWindow, tabId: string): void {
    const entry = this.get(win, tabId)
    if (!entry) return
    entry.selectedAt = this.now()
    this.selected.set(this.selectionKey(entry), entry)
  }

  trySuspend(win: BrowserWindow, tabId: string): boolean {
    const entry = this.get(win, tabId)
    if (!entry || this.protected(entry) || !this.checkpoint(entry)) return false
    this.suspend(entry)
    entry.page = undefined
    return true
  }

  enforce(win: BrowserWindow): void {
    if (this.enforcing) return
    this.enforcing = true
    try {
      const timer = this.timers.get(win.id)
      if (timer) clearTimeout(timer)
      this.timers.delete(win.id)
      const detached = this.entries(win).filter(entry => entry.page && !entry.visible)
      const scope = this.foreground.get(win.id)
      const priority = (entry: BrowserPageEntry) => entry.owner.projectKey === scope?.projectKey && entry.owner.threadId === scope.threadId
        ? 2 : this.selected.get(this.selectionKey(entry)) === entry ? 1 : 0
      let excess = detached.length - this.budget
      if (excess <= 0) return
      const candidates = detached.filter(entry => !this.protected(entry)).sort((a, b) => priority(a) - priority(b) || (a.selectedAt ?? 0) - (b.selectedAt ?? 0))
      for (const entry of candidates) {
        if (excess <= 0) break
        if (!this.checkpoint(entry)) continue
        this.suspend(entry)
        entry.page = undefined
        excess--
      }
      if (excess > 0) {
        const expires = detached.filter(entry => entry.selectedAt !== undefined && this.selected.get(this.selectionKey(entry)) === entry)
          .map(entry => entry.selectedAt! + 30 * 60 * 1000 - this.now()).filter(delay => delay > 0)
        if (expires.length) {
          const handle = setTimeout(() => this.enforce(win), Math.min(...expires))
          handle.unref?.()
          this.timers.set(win.id, handle)
        }
      }
    } finally { this.enforcing = false }
  }

  private protected(entry: BrowserPageEntry): boolean {
    const page = entry.page
    const scope = this.foreground.get(entry.win.id)
    const selected = this.selected.get(this.selectionKey(entry)) === entry
    const active = selected && scope?.projectKey === entry.owner.projectKey && scope.threadId === entry.owner.threadId
    return active || !page || entry.visible || entry.automation || entry.capture || entry.restoring ||
      (entry.selectedAt !== undefined && selected && this.now() - entry.selectedAt < 30 * 60 * 1000) ||
      page.isDestroyed() || page.isLoading() || page.isCurrentlyAudible() || page.isBeingCaptured() || this.hasDownload(entry.owner.tabId)
  }

  private selectionKey(entry: BrowserPageEntry): string { return JSON.stringify([entry.win.id, entry.owner.projectKey, entry.owner.threadId]) }
}

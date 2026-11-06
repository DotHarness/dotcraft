import { afterEach, describe, expect, it, vi } from 'vitest'
import { BrowserPageLifecycle, type BrowserPageEntry } from '../browserPageLifecycle'
import { BrowserPageSnapshotStore } from '../browserPageSnapshotStore'

vi.mock('../browserSessionPolicy', () => ({
  browserPageAuthority: (page: Electron.WebContents) => ({ kind: page.getURL().startsWith('file:') ? 'local-preview' : 'website' }),
  browserSessionKind: (url: string) => /^(file|dotcraft-viewer):/.test(url) ? 'local-preview' : 'website'
}))

function window(id: number) { return { id, isDestroyed: () => false } as Electron.BrowserWindow }
function fixture(budget = 32, limits?: { maxBytes: number }) {
  const store = new BrowserPageSnapshotStore(undefined, limits)
  let now = 0
  const suspended: BrowserPageEntry[] = []
  const downloading = new Set<string>()
  const lifecycle = new BrowserPageLifecycle(() => store, entry => { suspended.push(entry); entry.page!.close() }, tabId => downloading.has(tabId), () => now, budget)
  const win = window(1)
  lifecycle.registerPersistenceOwner(win)
  const add = (tabId: string, threadId = 'thread', initialUrl = 'https://example.com/', owner = win) => {
    const history = [{ url: initialUrl, title: tabId }, { url: `${initialUrl}next`, title: 'Next', pageState: 'state' }]
    const page = {
      close: vi.fn(), isDestroyed: () => false, isLoading: vi.fn(() => false), isCurrentlyAudible: vi.fn(() => false), isBeingCaptured: vi.fn(() => false),
      getURL: () => initialUrl, getTitle: () => tabId,
      navigationHistory: { getAllEntries: () => history, getActiveIndex: () => 1, restore: vi.fn(async () => {}) }
    } as unknown as Electron.WebContents
    const entry = lifecycle.prepare(owner, { tabId, threadId, workspacePath: '/workspace/a', projectKey: 'project:a', initialUrl })
    entry.page = page
    return entry
  }
  return { store, lifecycle, win, suspended, downloading, add, advance: (value: number) => { now += value } }
}
afterEach(() => vi.useRealTimers())

describe('browser page lifecycle', () => {
  it('keeps active automation alive beyond the detached budget and suspends only after release', () => {
    const f = fixture(1)
    const first = f.add('first'), second = f.add('second')
    first.automation = second.automation = true
    f.lifecycle.enforce(f.win)
    expect(f.suspended).toEqual([])
    first.automation = false
    f.lifecycle.enforce(f.win)
    expect(f.suspended).toEqual([first])
    expect(f.lifecycle.get(f.win, 'first')?.page).toBeUndefined()
    expect(f.store.get(first.browserStorageId, first.owner)?.navigation.index).toBe(1)
    expect(second.page).toBeDefined()
  })

  it('does not discard execution state when its snapshot exceeds the storage limit', () => {
    const f = fixture(0, { maxBytes: 100 })
    const entry = f.add('large')
    f.lifecycle.enforce(f.win)
    expect(f.suspended).toEqual([])
    expect(entry.page).toBeDefined()
    expect(f.store.owner(entry.browserStorageId)).toBeUndefined()
  })

  it('keeps inactive pages within the detached-page budget', () => {
    const f = fixture()
    for (let i = 0; i < 33; i++) f.add(`tab-${i}`, `thread-${i}`)
    f.lifecycle.enforce(f.win)
    expect(f.suspended).toHaveLength(1)
    expect(f.lifecycle.entries(f.win).filter(entry => entry.page)).toHaveLength(32)
    expect(f.lifecycle.entries(f.win)).toHaveLength(33)
  })

  it('protects recently selected pages for 30 minutes and rechecks at expiry without polling', () => {
    vi.useFakeTimers()
    const f = fixture(0)
    const page = f.add('selected')
    f.lifecycle.select(f.win, 'selected')
    f.lifecycle.enforce(f.win)
    expect(f.suspended).toEqual([])
    f.advance(30 * 60 * 1000 - 1)
    vi.advanceTimersByTime(30 * 60 * 1000 - 1)
    expect(f.suspended).toEqual([])
    f.advance(1); vi.advanceTimersByTime(1)
    expect(f.suspended).toEqual([page])
    f.lifecycle.disposeWindow(f.win)
  })

  it('protects the current thread active page even after working-set expiry while hidden', () => {
    const f = fixture(0)
    const entry = f.add('active')
    f.lifecycle.select(f.win, 'active')
    f.advance(31 * 60 * 1000)
    f.lifecycle.setForeground(f.win, { projectKey: 'project:a', threadId: 'thread' })
    expect(f.lifecycle.trySuspend(f.win, 'active')).toBe(false)
    expect(f.suspended).toEqual([])
    f.lifecycle.setForeground(f.win, { projectKey: 'project:a', threadId: 'other' })
    expect(f.suspended).toEqual([entry])
  })

  it.each(['visible', 'capture', 'restoring', 'audible', 'loading', 'media', 'download'] as const)('does not suspend a page protected by %s', protection => {
    const f = fixture(0)
    const entry = f.add('protected')
    if (protection === 'visible' || protection === 'capture' || protection === 'restoring') entry[protection] = true
    if (protection === 'audible') vi.mocked(entry.page!.isCurrentlyAudible).mockReturnValue(true)
    if (protection === 'loading') vi.mocked(entry.page!.isLoading).mockReturnValue(true)
    if (protection === 'media') vi.mocked(entry.page!.isBeingCaptured).mockReturnValue(true)
    if (protection === 'download') f.downloading.add(entry.owner.tabId)
    f.lifecycle.enforce(f.win)
    expect(f.suspended).toEqual([])
    expect(f.lifecycle.trySuspend(f.win, entry.owner.tabId)).toBe(false)
  })

  it('prioritizes inactive secondary, inactive selected then current-thread secondary pages', () => {
    const f = fixture(1)
    const current = f.add('current-secondary', 'current'), selected = f.add('inactive-selected', 'other'), secondary = f.add('inactive-secondary', 'other')
    f.lifecycle.select(f.win, selected.owner.tabId)
    f.advance(30 * 60 * 1000)
    f.lifecycle.setForeground(f.win, { projectKey: 'project:a', threadId: 'current' })
    expect(f.suspended).toEqual([secondary, selected])
    expect(current.page).toBeDefined()
  })

  it('teardown preserves snapshots while explicit close deletes them', () => {
    const f = fixture()
    const kept = f.add('kept'), closed = f.add('closed')
    f.lifecycle.checkpoint(closed)
    f.lifecycle.close(f.win, 'closed', true)
    f.lifecycle.disposeWindow(f.win)
    expect(f.store.owner(closed.browserStorageId)).toBeUndefined()
    expect(f.store.get(kept.browserStorageId, kept.owner)?.title).toBe('kept')
  })

  it('restores lazily, distinguishes a live page and rejects another window or owner', () => {
    const f = fixture()
    const entry = f.add('one')
    f.lifecycle.checkpoint(entry)
    const params = { workspacePath: '/workspace/a', projectKey: 'project:a', threadId: 'thread', tabs: [{ tabId: 'one', browserStorageId: entry.browserStorageId }] }
    expect(f.lifecycle.restoreState(f.win, params)[0].status).toBe('already-live')
    expect(f.lifecycle.restoreState(window(2), params)[0].status).toBe('owned-by-another-window')
    f.lifecycle.disposeWindow(f.win)
    const result = f.lifecycle.restoreState(f.win, params)[0]
    expect(result).toMatchObject({ status: 'snapshot-ready', suspended: true, canGoBack: true, canGoForward: false })
    expect(f.lifecycle.get(f.win, 'one')?.page).toBeUndefined()
    expect(f.lifecycle.restoreState(f.win, { ...params, projectKey: 'project:b' })[0].status).toBe('owned-by-another-window')
  })

  it('does not bypass other-window ownership when the saved snapshot is missing', () => {
    const f = fixture()
    const other = window(2)
    const params = { tabId: 'missing', workspacePath: '/workspace/a', threadId: 'thread', browserStorageId: 'missing-snapshot' }
    expect(f.lifecycle.restoreState(other, { ...params, tabs: [params] })[0].status).toBe('owned-by-another-window')
    expect(() => f.lifecycle.prepare(other, params)).toThrow('owned by another window')
    expect(f.lifecycle.entries(other)).toEqual([])
  })

  it('does not replace a live tab storage identity with a stale checkpoint identity', () => {
    const f = fixture()
    const entry = f.add('live')
    expect(() => f.lifecycle.prepare(f.win, { ...entry.params, browserStorageId: 'stale-id' })).toThrow('owner does not match')
    expect(f.lifecycle.get(f.win, 'live')).toBe(entry)
  })

  it('does not revive an explicitly closed storage identity through a late restore', () => {
    const f = fixture()
    const entry = f.add('closed')
    f.lifecycle.checkpoint(entry)
    f.lifecycle.close(f.win, 'closed', true)
    expect(f.lifecycle.restoreState(f.win, { workspacePath: '/workspace/a', projectKey: 'project:a', threadId: 'thread', tabs: [{ tabId: 'closed', browserStorageId: entry.browserStorageId }] })[0].status).toBe('missing')
    expect(f.lifecycle.get(f.win, 'closed')).toBeUndefined()
    expect(() => f.lifecycle.prepare(f.win, entry.params)).toThrow('closed')
  })

  it('uses Electron history restore without replaying individual navigation entries', async () => {
    const f = fixture()
    const entry = f.add('one')
    f.lifecycle.checkpoint(entry)
    await f.lifecycle.restore(entry, entry.page!)
    expect(entry.page!.navigationHistory.restore).toHaveBeenCalledWith({ index: 1, entries: [{ url: 'https://example.com/', title: 'one' }, { url: 'https://example.com/next', title: 'Next', pageState: 'state' }] })
    entry.snapshot!.navigation.entries[0].url = 'javascript:alert(1)'
    await expect(f.lifecycle.restore(entry, entry.page!)).rejects.toThrow('unsupported navigation')
  })

  it('reports native restore failure without logging page state and permits a validated URL fallback', async () => {
    const f = fixture()
    const entry = f.add('native-failure')
    f.lifecycle.checkpoint(entry)
    vi.mocked(entry.page!.navigationHistory.restore).mockRejectedValue(new Error('Native restore rejected sensitive-page-state'))
    const warning = vi.spyOn(console, 'warn').mockImplementation(() => {})
    try {
      await expect(f.lifecycle.restore(entry, entry.page!)).resolves.toBe(false)
      expect(JSON.stringify(warning.mock.calls)).not.toContain('sensitive-page-state')
    } finally { warning.mockRestore() }
  })

  it('requires original local-preview authorization and suppresses a stale restore after close', async () => {
    const f = fixture()
    const entry = f.add('local', 'thread', 'file:///workspace/a/index.html')
    f.lifecycle.checkpoint(entry)
    await expect(f.lifecycle.restore(entry, entry.page!)).rejects.toThrow('requires file authorization')
    let authorize!: () => void
    const validator = vi.fn(() => new Promise<void>(resolve => { authorize = resolve }))
    f.lifecycle.setRestoreValidator(validator)
    const result = f.lifecycle.restore(entry, entry.page!)
    f.lifecycle.close(f.win, 'local', true)
    authorize()
    await expect(result).rejects.toThrow('closed')
    expect(validator).toHaveBeenCalledWith(f.win, 'file:///workspace/a/index.html', expect.objectContaining({ workspacePath: '/workspace/a', projectKey: 'project:a', threadId: 'thread' }))
    expect(entry.page!.navigationHistory.restore).not.toHaveBeenCalled()
  })
})

import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { BrowserRestoreResult } from '../../shared/viewer/types'
import type { PanelCheckpoint, PanelScope } from '../../shared/viewer/panelState'
import { useDetailPanelStore, type PanelSession } from '../stores/detailPanelStore'
import { startDetailPanelPersistence } from '../stores/detailPanelPersistence'
const scope: PanelScope = {
  projectKey: '/project-a',
  threadId: 'thread-a'
}
const store = () => useDetailPanelStore.getState()
const saved = (): PanelCheckpoint => ({
  version: 1,
  tabs: [{
    id: 'browser',
    kind: 'browser',
    browserStorageId: 'storage-a',
    target: 'browser',
    label: 'Browser',
    currentUrl: 'https://example.com/',
    loading: false,
    canGoBack: false,
    canGoForward: false
  }],
  systemTabs: ['plan'],
  activeTab: {
    kind: 'system',
    id: 'plan'
  },
  visible: true,
  fullView: true,
  width: 640
})
const managers: Array<ReturnType<typeof startDetailPanelPersistence>> = []

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => {
    resolve = done
  })
  return {
    promise,
    resolve
  }
}

function setup(read: () => Promise<PanelCheckpoint | null>, restore: (scope: PanelScope, panel: PanelSession) => Promise<BrowserRestoreResult[]> = vi.fn(async () => [])) {
  const update = vi.fn(async (_scope: PanelScope, _checkpoint: PanelCheckpoint | null) => { })
  const manager = startDetailPanelPersistence({
    read,
    update
  }, restore)
  managers.push(manager)
  return {
    manager,
    update,
    restore
  }
}
beforeEach(() => {
  store().reset()
  store().onWorkspaceSwitched(scope.projectKey)
  store().onThreadSwitched(scope.threadId)
})
afterEach(() => {
  for (const manager of managers.splice(0)) manager.dispose()
})
describe('panel checkpoint hydration', () => {
  it('does not overwrite saved data with the initial empty panel and registers hidden browser routes', async () => {
    const read = deferred<PanelCheckpoint | null>()
    const fixture = setup(() => read.promise)
    const pending = fixture.manager.hydrate(scope)
    expect(fixture.update).not.toHaveBeenCalled()
    read.resolve(saved())
    await pending
    expect(store().getCurrentPanel()).toMatchObject({
      openSystemTabs: ['plan'],
      activeDetailTab: {
        kind: 'system',
        id: 'plan'
      },
      detailPanelPreferredVisible: true,
      detailPanelFullView: true,
      detailPanelWidth: 640
    })
    expect(fixture.restore).toHaveBeenCalledWith(scope, expect.objectContaining({
      tabs: [expect.objectContaining({
        browserStorageId: 'storage-a'
      })]
    }))
    expect(fixture.update).toHaveBeenCalledWith(scope, expect.objectContaining({
      activeTab: {
        kind: 'system',
        id: 'plan'
      }
    }))
  })
  it('publishes restored intent before browser registration while preserving a concurrent fresh tab', async () => {
    const read = deferred<PanelCheckpoint | null>()
    const registration = deferred<BrowserRestoreResult[]>()
    const published = deferred<PanelSession>()
    const restore = vi.fn((_scope: PanelScope, _panel: PanelSession) => registration.promise)
    const fixture = setup(() => read.promise, restore)
    const pending = fixture.manager.hydrate(scope)
    const freshId = store().openBrowser({ threadId: scope.threadId })
    const unsubscribe = useDetailPanelStore.subscribe(state => {
      const panel = state.getPanel(scope.threadId, scope.projectKey)
      if (panel.tabs.some(tab => tab.id === 'browser')) published.resolve(panel)
    })
    try {
      read.resolve(saved())
      const panel = await published.promise
      expect(panel.tabs.find(tab => tab.id === 'browser')).toMatchObject({ browserStorageId: 'storage-a', restore: 'required' })
      expect(panel.tabs.find(tab => tab.id === freshId)).toMatchObject({ restore: 'none' })
      expect(restore).toHaveBeenCalledWith(scope, panel)
      expect(fixture.update).not.toHaveBeenCalled()
      registration.resolve([])
      await pending
      const checkpoint = fixture.update.mock.calls.at(-1)?.[1]!
      expect(checkpoint.tabs.every(tab => !('restore' in tab))).toBe(true)
    } finally {
      unsubscribe()
      registration.resolve([])
      await pending
    }
  })

  it('restores only the owning scope after the user switches to another project', async () => {
    const read = deferred<PanelCheckpoint | null>()
    const fixture = setup(() => read.promise)
    const pending = fixture.manager.hydrate(scope)
    store().onWorkspaceSwitched('/project-b')
    store().onThreadSwitched('thread-a')
    store().setActiveDetailTab('board')
    read.resolve(saved())
    await pending
    expect(store().getCurrentPanel().activeDetailTab).toEqual({
      kind: 'system',
      id: 'board'
    })
    expect(store().getPanel(scope.threadId, scope.projectKey).tabs).toHaveLength(1)
  })
  it('does not resurrect a tab closed during an outstanding read', async () => {
    store().openBrowser({
      threadId: scope.threadId,
      tabId: 'browser'
    })
    const read = deferred<PanelCheckpoint | null>()
    const fixture = setup(() => read.promise)
    const pending = fixture.manager.hydrate(scope)
    store().closeTab(scope.threadId, 'browser')
    read.resolve(saved())
    await pending
    expect(store().getCurrentPanel().tabs).toEqual([])
  })
  it('rejects a late read after deletion even when no panel was mounted', async () => {
    const read = deferred<PanelCheckpoint | null>()
    const fixture = setup(() => read.promise)
    const pending = fixture.manager.hydrate(scope)
    store().onThreadDeleted(scope.threadId)
    read.resolve(saved())
    await pending
    expect(store().getCurrentPanel().tabs).toEqual([])
    expect(fixture.update).toHaveBeenCalledExactlyOnceWith(scope, null)
    expect(fixture.restore).not.toHaveBeenCalled()
  })
  it('preserves a local edit while merging saved routes', async () => {
    const read = deferred<PanelCheckpoint | null>()
    const fixture = setup(() => read.promise)
    const pending = fixture.manager.hydrate(scope)
    const tabId = store().openFile({
      threadId: scope.threadId,
      absolutePath: '/project-a/new.ts',
      relativePath: 'new.ts',
      contentClass: 'text'
    })
    store().setActiveViewerTab(tabId)
    read.resolve(saved())
    await pending
    expect(store().getCurrentPanel().tabs.map(tab => tab.kind)).toEqual(['browser', 'file'])
    expect(store().getCurrentPanel().openSystemTabs).toEqual(['plan'])
    expect(store().getCurrentPanel().activeDetailTab).toEqual({
      kind: 'viewer',
      id: tabId
    })
  })
  it('strips terminal commands, PID and environment rather than replaying them on reconstruction', async () => {
    const fixture = setup(async () => null)
    await fixture.manager.hydrate(scope)
    store().runInTerminal({
      threadId: scope.threadId,
      tabId: 'terminal',
      cwd: '/project-a',
      title: 'Terminal',
      command: 'dangerous',
      env: {
        TOKEN: 'secret'
      }
    })
    store().updateTerminalTab(scope.threadId, 'terminal', {
      hasStarted: true,
      pid: 123
    })
    const checkpoint = fixture.update.mock.calls.at(-1)?.[1] as PanelCheckpoint
    expect(checkpoint.tabs[0]).toEqual({
      id: 'terminal',
      kind: 'terminal',
      label: 'Terminal',
      title: 'Terminal',
      cwd: '/project-a',
      hasStarted: false
    })
    expect(JSON.stringify(checkpoint)).not.toContain('dangerous')
    expect(JSON.stringify(checkpoint)).not.toContain('secret')
  })
  it('moves a hydrated Welcome checkpoint to its thread without retaining a duplicate draft', async () => {
    const welcome = {
      ...scope,
      threadId: 'welcome:/project-a'
    }
    store().onThreadSwitched(welcome.threadId)
    const fixture = setup(async () => saved())
    await fixture.manager.hydrate(welcome)
    fixture.update.mockClear()
    store().handOffWelcomePanel(welcome.threadId, scope.threadId)
    expect(fixture.update).toHaveBeenCalledWith(welcome, null)
    expect(fixture.update).toHaveBeenCalledWith(scope, expect.objectContaining({
      tabs: [expect.objectContaining({
        id: 'browser'
      })]
    }))
    expect(store().getPanel(welcome.threadId).tabs).toEqual([])
  })
})

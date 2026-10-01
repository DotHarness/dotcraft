import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'

vi.mock('electron', () => ({
  app: { getAppPath: () => process.cwd() },
  BrowserWindow: vi.fn(),
  WebContentsView: vi.fn(),
  nativeImage: {},
  session: { fromPartition: vi.fn() },
  shell: {}
}))

import { BrowserUseManager } from '../browserUseManager'
import { BrowserUseViewports, normalizeViewportSize } from '../browserUseViewport'

const flush = () => new Promise<void>(resolve => setTimeout(resolve, 0))

describe('normalizeViewportSize', () => {
  it('rounds each dimension and clamps it to its range', () => {
    expect(normalizeViewportSize(100.4, 99999)).toEqual({ width: 240, height: 4096 })
    expect(normalizeViewportSize(5000, 120)).toEqual({ width: 4096, height: 160 })
    expect(normalizeViewportSize(1024.6, 768.4)).toEqual({ width: 1025, height: 768 })
  })

  it('rejects sizes that are not numbers', () => {
    expect(() => normalizeViewportSize(undefined, 600)).toThrow('InvalidArgument')
    expect(() => normalizeViewportSize(800, Number.NaN)).toThrow('InvalidArgument')
  })
})

describe('BrowserUseViewports', () => {
  function create() {
    const tab = {}
    const sent: Array<[string, Record<string, unknown> | undefined]> = []
    const presented: Array<{ width: number; height: number } | undefined> = []
    let gate: Promise<void> = Promise.resolve()
    let failNext = false
    const viewports = new BrowserUseViewports<object>({
      send: async (_tab, method, params) => {
        await gate
        if (failNext) {
          failNext = false
          throw new Error('Debugger is not attached')
        }
        sent.push([method, params])
      },
      present: (_tab, size) => { presented.push(size) }
    })
    const hold = () => {
      let open!: () => void
      gate = new Promise(resolve => { open = resolve })
      return open
    }
    return { tab, viewports, sent, presented, hold, failNext: () => { failNext = true } }
  }

  const size = { width: 900, height: 640 }

  it('emulates the size at scale 1 without mobile mode and reset clears only a set viewport', async () => {
    const { tab, viewports, sent, presented } = create()
    await viewports.reset(tab)
    expect(sent).toEqual([])

    await viewports.set(tab, size)
    expect(sent).toEqual([['Emulation.setDeviceMetricsOverride', { deviceScaleFactor: 1, mobile: false, width: 900, height: 640 }]])

    await viewports.reset(tab)
    await viewports.reset(tab)
    expect(sent.map(([method]) => method)).toEqual(['Emulation.setDeviceMetricsOverride', 'Emulation.clearDeviceMetricsOverride'])
    expect(presented).toEqual([size, undefined])
  })

  it('skips an update superseded by a newer one while keeping the order of applied updates', async () => {
    const { tab, viewports, sent, hold } = create()
    const open = hold()
    const first = viewports.set(tab, { width: 300, height: 200 })
    await flush()
    const second = viewports.set(tab, { width: 400, height: 300 })
    const third = viewports.set(tab, { width: 500, height: 400 })
    open()
    await Promise.all([first, second, third])
    expect(sent.map(([, params]) => params?.width)).toEqual([300, 500])
  })

  it('settles only after pending updates have been applied', async () => {
    const { tab, viewports, sent, hold } = create()
    const open = hold()
    const done = vi.fn()
    const pending = viewports.set(tab, size)
    void viewports.settled(tab).then(done)
    await flush()
    expect(done).not.toHaveBeenCalled()
    open()
    await pending
    await viewports.settled(tab)
    expect(done).toHaveBeenCalled()
    expect(sent).toHaveLength(1)
  })

  it('reapplies an explicit viewport when the debugger attaches again and does nothing without one', async () => {
    const { tab, viewports, sent } = create()
    viewports.attached(tab)
    await viewports.settled(tab)
    expect(sent).toEqual([])

    await viewports.set(tab, size)
    viewports.attached(tab)
    await viewports.settled(tab)
    expect(sent.map(([method]) => method)).toEqual(['Emulation.setDeviceMetricsOverride', 'Emulation.setDeviceMetricsOverride'])
  })

  it('reports a failed update to its caller and keeps accepting updates', async () => {
    const { tab, viewports, sent, failNext } = create()
    failNext()
    await expect(viewports.set(tab, size)).rejects.toThrow('Debugger is not attached')
    await viewports.set(tab, { width: 500, height: 400 })
    expect(sent.map(([, params]) => params?.width)).toEqual([500])
  })

  it('drops the explicit viewport on release and skips updates still queued', async () => {
    const { tab, viewports, sent, presented, hold } = create()
    const open = hold()
    const pending = viewports.set(tab, size)
    viewports.release(tab)
    open()
    await pending
    expect(sent).toEqual([])
    expect(presented).toEqual([size, undefined])
  })
})

describe('BrowserUseManager viewport', () => {
  const activeManagers = new Set<BrowserUseManager>()
  afterEach(async () => {
    await Promise.all([...activeManagers].map(manager => manager.closeBackendForTests()))
    activeManagers.clear()
  })

  function createHarness() {
    type Sent = { tabId: string; method: string; params?: Record<string, unknown> }
    const sent: Sent[] = []
    const gates = new Map<string, Promise<void>>()
    const pages = new Map<string, ReturnType<typeof createPage>>()
    const userTabs: string[] = []

    function createPage(tabId: string) {
      const emitter = new EventEmitter()
      let attached = false
      const debuggerApi = Object.assign(new EventEmitter(), {
        isAttached: () => attached,
        attach: vi.fn(() => { attached = true }),
        detach: vi.fn(() => {
          attached = false
          debuggerApi.emit('detach', {}, 'detached')
        }),
        sendCommand: vi.fn(async (method: string, params?: Record<string, unknown>) => {
          sent.push({ tabId, method, params })
          await gates.get(method)
          return {}
        })
      })
      return Object.assign(emitter, {
        isDestroyed: () => false,
        getURL: () => 'about:blank',
        getTitle: () => 'Page',
        isLoading: () => false,
        stop: vi.fn(),
        debugger: debuggerApi
      })
    }

    const owner = Object.assign(new EventEmitter(), {
      id: 1,
      isDestroyed: () => false,
      getTitle: () => 'Viewport fixture',
      webContents: { isDestroyed: () => false, send: vi.fn() }
    }) as unknown as Electron.BrowserWindow
    const snapshot = (tabId: string) => ({ tabId, currentUrl: 'about:blank', title: 'Page', loading: false })
    const host = {
      setCaptureSurface: vi.fn(),
      createAutomationTab: vi.fn((_owner: unknown, params: { tabId: string }) => { pages.set(params.tabId, createPage(params.tabId)) }),
      getTabWebContents: (_owner: unknown, tabId: string) => pages.get(tabId) ?? null,
      listAutomationTargetTabs: () => userTabs.map(snapshot),
      loadAutomationUrl: vi.fn(),
      destroyTab: vi.fn((_owner: unknown, tabId: string) => { pages.delete(tabId) }),
      snapshotState: (_owner: unknown, tabId: string) => snapshot(tabId),
      setAutomationState: vi.fn(),
      setViewport: vi.fn(),
      getLayoutSize: vi.fn(() => ({ width: 1280, height: 720 })),
      isVisible: vi.fn(() => false),
      setVisible: vi.fn(),
      moveMouse: vi.fn()
    }

    async function start() {
      const manager = new BrowserUseManager(host as unknown as ConstructorParameters<typeof BrowserUseManager>[0])
      activeManagers.add(manager)
      await manager.prepareNodeRepl(owner, {
        threadId: 'thread-viewport',
        evaluationId: 'eval-1',
        browserSession: { sessionId: 'session-viewport', turnId: 'turn-1' }
      })
      const session = { session_id: 'session-viewport', turn_id: 'turn-1' }
      const call = (method: string, params: Record<string, unknown> = {}) =>
        manager.handleBrowserUseBackendRequest(method, { ...session, ...params })
      const command = (type: string, params: Record<string, unknown> = {}) =>
        call('executeUnhandledCommand', { type, ...params })
      const createTab = async () => await call('createTab') as { id: number }
      const endTurn = () => manager.handleTurnNotification('turn/completed', { threadId: 'thread-viewport', turn: { id: 'turn-1' } })
      return { call, command, createTab, endTurn }
    }

    const viewerId = (index: number) => host.createAutomationTab.mock.calls[index]![1].tabId
    const emulation = () => sent.filter(item => item.method.startsWith('Emulation.'))
    const override = (tabId: string, width: number, height: number) => ({
      tabId,
      method: 'Emulation.setDeviceMetricsOverride',
      params: { deviceScaleFactor: 1, mobile: false, width, height }
    })
    const hold = (method: string) => {
      let open!: () => void
      gates.set(method, new Promise(resolve => { open = resolve }))
      return open
    }
    return { owner, host, pages, userTabs, sent, start, viewerId, emulation, override, hold, createPage }
  }

  it('applies a viewport to the selected tab and falls back to the first controlled tab', async () => {
    const h = createHarness()
    const { command, createTab } = await h.start()
    await createTab()
    const second = await createTab()

    await command('browser_viewport_set', { width: 900, height: 640 })
    expect(h.emulation()).toEqual([h.override(h.viewerId(1), 900, 640)])
    expect(h.host.setViewport.mock.calls).toEqual([[h.owner, { tabId: h.viewerId(1), viewport: { width: 900, height: 640 } }]])

    await command('close_tab', { tab_id: second.id })
    await command('browser_viewport_set', { width: 800, height: 600 })
    expect(h.emulation().at(-1)).toEqual(h.override(h.viewerId(0), 800, 600))
  })

  it('holds a request without a controlled tab for the next created tab only', async () => {
    const h = createHarness()
    const { command, createTab } = await h.start()

    await command('browser_viewport_set', { width: 900, height: 640 })
    expect(h.sent).toEqual([])

    await createTab()
    await createTab()
    expect(h.emulation()).toEqual([h.override(h.viewerId(0), 900, 640)])
    expect(h.host.setViewport).toHaveBeenCalledTimes(1)
  })

  it('lets a held reset cancel a held set', async () => {
    const h = createHarness()
    const { command, createTab } = await h.start()
    await command('browser_viewport_set', { width: 900, height: 640 })
    await command('browser_viewport_reset')
    await createTab()
    expect(h.emulation()).toEqual([])
  })

  it('discards a held request when the turn ends', async () => {
    const h = createHarness()
    const { command, createTab, endTurn } = await h.start()
    await command('browser_viewport_set', { width: 900, height: 640 })
    endTurn()
    await createTab()
    expect(h.emulation()).toEqual([])
  })

  it('applies a held request to a claimed user tab and clears it when the tab is released', async () => {
    const h = createHarness()
    h.pages.set('user-tab', h.createPage('user-tab'))
    h.userTabs.push('user-tab')
    const { call, command, endTurn } = await h.start()
    await command('browser_viewport_set', { width: 900, height: 640 })
    const [listed] = await call('getUserTabs') as Array<{ id: number }>
    expect(h.emulation()).toEqual([])

    await call('claimUserTab', { tabId: listed!.id })
    expect(h.emulation()).toEqual([h.override('user-tab', 900, 640)])

    endTurn()
    expect(h.host.setViewport).toHaveBeenLastCalledWith(h.owner, { tabId: 'user-tab', viewport: undefined })
    expect(h.pages.get('user-tab')!.debugger.detach).toHaveBeenCalled()
  })

  it('keeps the viewport of a retained tab and reapplies it when the debugger attaches again', async () => {
    const h = createHarness()
    const { call, command, createTab, endTurn } = await h.start()
    const tab = await createTab()
    await command('browser_viewport_set', { width: 900, height: 640 })
    await command('tab_mark', { tabId: tab.id, status: 'handoff' })

    endTurn()
    expect(h.pages.get(h.viewerId(0))!.debugger.detach).toHaveBeenCalled()
    expect(h.host.setViewport).toHaveBeenCalledTimes(1)

    await call('executeCdp', { method: 'Runtime.evaluate', params: { expression: '1' }, target: { tabId: tab.id } })
    expect(h.sent.map(item => item.method)).toEqual([
      'Emulation.setDeviceMetricsOverride',
      'Emulation.setDeviceMetricsOverride',
      'Runtime.evaluate'
    ])
  })

  it('makes CDP commands wait for pending emulation and resolves the viewport request after it finished', async () => {
    const h = createHarness()
    const { call, command, createTab } = await h.start()
    const tab = await createTab()
    const open = h.hold('Emulation.setDeviceMetricsOverride')

    const done = vi.fn()
    const set = command('browser_viewport_set', { width: 900, height: 640 }).then(done)
    await flush()
    const evaluate = call('executeCdp', { method: 'Runtime.evaluate', params: { expression: '1' }, target: { tabId: tab.id } })
    await flush()
    expect(h.sent.map(item => item.method)).toEqual(['Emulation.setDeviceMetricsOverride'])
    expect(done).not.toHaveBeenCalled()

    open()
    await Promise.all([set, evaluate])
    expect(h.sent.map(item => item.method)).toEqual(['Emulation.setDeviceMetricsOverride', 'Runtime.evaluate'])
    expect(done).toHaveBeenCalled()
  })
})

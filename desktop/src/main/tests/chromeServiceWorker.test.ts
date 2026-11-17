import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import vm from 'node:vm'
import { afterEach, describe, expect, it, vi } from 'vitest'

type ChromeTab = {
  id: number
  windowId: number
  url?: string
  title?: string
  status?: string
  active?: boolean
}

type Listener = (...args: any[]) => unknown

function chromeEvent() {
  const listeners: Listener[] = []
  return {
    addListener: (listener: Listener) => { listeners.push(listener) },
    fire: (...args: unknown[]) => listeners.map((listener) => listener(...args))
  }
}

type RelayResponse = { type: string; id: number; result?: any; error?: { message: string } }

const serviceWorkerPath = path.resolve(
  path.dirname(fileURLToPath(import.meta.url)),
  '../../../resources/plugins/dotcraft-bundled/plugins/chrome/extension/service_worker.js'
)

function loadWorker(initialTabs: ChromeTab[] = []) {
  const tabs = new Map(initialTabs.map((tab) => [tab.id, { ...tab }]))
  const posted: any[] = []
  const waiters = new Map<number, (message: RelayResponse) => void>()
  const ports: Array<{ onMessage: ReturnType<typeof chromeEvent>; onDisconnect: ReturnType<typeof chromeEvent>; postMessage: (message: any) => void }> = []
  const overlayLog: string[] = []
  let nextGroupId = 100
  const chrome = {
    runtime: {
      connectNative: vi.fn(() => {
        const port = {
          onMessage: chromeEvent(),
          onDisconnect: chromeEvent(),
          postMessage: (message: any) => {
            posted.push(message)
            if (message?.type === 'relay-response') waiters.get(message.id)?.(message)
          }
        }
        ports.push(port)
        return port
      }),
      getManifest: () => ({ version: '0.2.0' }),
      lastError: null as null | { message: string },
      onInstalled: chromeEvent(),
      onStartup: chromeEvent(),
      onMessage: chromeEvent()
    },
    alarms: {
      create: vi.fn(async () => undefined),
      clear: vi.fn(async () => true),
      onAlarm: chromeEvent()
    },
    windows: {
      getLastFocused: vi.fn(async () => ({ id: 1 })),
      get: vi.fn(async () => ({ id: 1, focused: true }))
    },
    tabs: {
      get: vi.fn(async (tabId: number) => {
        const tab = tabs.get(tabId)
        if (!tab) throw new Error(`No tab with id: ${tabId}.`)
        return { ...tab }
      }),
      query: vi.fn(async () => [...tabs.values()].map((tab) => ({ ...tab }))),
      create: vi.fn(async (options: Record<string, unknown>) => {
        const id = Math.max(0, ...tabs.keys()) + 1
        const tab = { id, windowId: Number(options.windowId ?? 1), url: String(options.url), title: '', status: 'complete', active: options.active === true }
        tabs.set(id, tab)
        return { ...tab }
      }),
      update: vi.fn(async (tabId: number, update: Record<string, unknown>) => {
        const tab = { ...tabs.get(tabId)!, ...update }
        tabs.set(tabId, tab as ChromeTab)
        return { ...tab }
      }),
      remove: vi.fn(async (tabId: number) => { tabs.delete(tabId) }),
      group: vi.fn(async (options: { groupId?: number }) => options.groupId ?? nextGroupId++),
      sendMessage: vi.fn(async (_tabId: number, message: Record<string, unknown>) => {
        overlayLog.push(message.action === 'move' ? `move:${message.sequence}` : `state:${message.active}:${message.suppressed}`)
        return { ok: true }
      }),
      onUpdated: chromeEvent(),
      onActivated: chromeEvent(),
      onRemoved: chromeEvent()
    },
    tabGroups: {
      get: vi.fn(async (groupId: number) => ({ id: groupId })),
      update: vi.fn(async () => undefined)
    },
    scripting: {
      executeScript: vi.fn(async () => [])
    },
    debugger: {
      attach: vi.fn(async () => undefined),
      detach: vi.fn(async () => undefined),
      sendCommand: vi.fn(async (): Promise<unknown> => ({})),
      onEvent: chromeEvent(),
      onDetach: chromeEvent()
    }
  }
  const context = vm.createContext({
    chrome, console, setTimeout: globalThis.setTimeout, clearTimeout: globalThis.clearTimeout,
    URL, Error, Promise, Map, Set, JSON, Math, Number, String, Boolean, Object, Array, RegExp
  })
  vm.runInContext(fs.readFileSync(serviceWorkerPath, 'utf8'), context, { filename: serviceWorkerPath })
  let nextId = 1
  const port = () => ports[ports.length - 1]!
  return {
    chrome,
    tabs,
    ports,
    posted,
    overlayLog,
    events: (name: string) => posted.filter((message) => message.type === 'relay-event' && message.event === name).map((message) => message.params),
    async request(method: string, params: Record<string, unknown> = {}) {
      const id = nextId++
      const response = new Promise<RelayResponse>((resolve) => waiters.set(id, resolve))
      port().onMessage.fire({ type: 'relay-request', id, method, params })
      return await response
    },
    sendNative(message: Record<string, unknown>) {
      port().onMessage.fire(message)
    }
  }
}

const flush = () => new Promise<void>((resolve) => setImmediate(resolve))
const webTab = (id: number, url = `https://example.test/${id}`): ChromeTab => ({ id, windowId: 1, url, title: `Tab ${id}`, status: 'complete', active: id === 1 })

afterEach(() => {
  vi.useRealTimers()
})

describe('chrome extension relay', () => {
  it('connects at startup and holds one debugger attachment across commands', async () => {
    const worker = loadWorker([webTab(7)])
    expect(worker.chrome.runtime.connectNative).toHaveBeenCalledTimes(1)

    expect((await worker.request('hello')).result).toEqual({ protocolVersion: 4, extensionVersion: '0.2.0' })
    expect((await worker.request('debugger.attach', { tabId: 7 })).result).toEqual({ attached: true })
    expect((await worker.request('debugger.attach', { tabId: 7 })).result).toEqual({ attached: true })
    worker.chrome.debugger.sendCommand.mockResolvedValueOnce({ result: { value: 2 } })
    expect((await worker.request('debugger.send', { tabId: 7, method: 'Runtime.evaluate', params: { expression: '1+1' } })).result)
      .toEqual({ result: { value: 2 } })
    await worker.request('debugger.send', { tabId: 7, sessionId: 'frame-1', method: 'Runtime.enable' })
    await worker.request('debugger.send', { tabId: 7, method: 'Page.enable' })

    expect(worker.chrome.debugger.attach).toHaveBeenCalledTimes(1)
    expect(worker.chrome.debugger.detach).not.toHaveBeenCalled()
    expect(worker.chrome.debugger.sendCommand.mock.calls.map(([target, method]) => [target, method])).toEqual([
      [{ tabId: 7 }, 'Runtime.evaluate'],
      [{ tabId: 7, sessionId: 'frame-1' }, 'Runtime.enable'],
      [{ tabId: 7 }, 'Page.enable']
    ])
  })

  it('rejects unheld tabs, protected pages, and foreign debuggers', async () => {
    const worker = loadWorker([webTab(1), webTab(2, 'chrome://extensions/'), webTab(3), webTab(4)])

    expect((await worker.request('debugger.send', { tabId: 1, method: 'Page.enable' })).error?.message).toMatch(/^DebuggerUnavailable:/)
    expect((await worker.request('debugger.attach', { tabId: 2 })).error?.message).toMatch(/^DebuggerUnavailable:/)

    worker.chrome.debugger.attach.mockRejectedValueOnce(new Error('Another debugger is already attached to the tab with id: 3.'))
    worker.chrome.debugger.detach.mockRejectedValueOnce(new Error('Debugger is not attached to the tab with id: 3.'))
    expect((await worker.request('debugger.attach', { tabId: 3 })).error?.message)
      .toBe('DebuggerUnavailable: Another debugger is attached to this tab. Close DevTools or other tools debugging it.')

    worker.chrome.debugger.attach.mockRejectedValueOnce(new Error('Another debugger is already attached to the tab with id: 4.'))
    expect((await worker.request('debugger.attach', { tabId: 4 })).result).toEqual({ attached: true })
  })

  it('forwards CDP events and a user cancellation, after which commands need a new attach', async () => {
    const worker = loadWorker([webTab(7)])
    await worker.request('debugger.attach', { tabId: 7 })

    worker.chrome.debugger.onEvent.fire({ tabId: 7, sessionId: 'frame-1' }, 'Page.loadEventFired', { timestamp: 1 })
    worker.chrome.debugger.onEvent.fire({ tabId: 7 }, 'Page.domContentEventFired', { timestamp: 2 })
    worker.chrome.debugger.onDetach.fire({ tabId: 7 }, 'canceled_by_user')

    expect(worker.events('debugger.event')).toEqual([
      { tabId: 7, sessionId: 'frame-1', method: 'Page.loadEventFired', params: { timestamp: 1 } },
      { tabId: 7, method: 'Page.domContentEventFired', params: { timestamp: 2 } }
    ])
    expect(worker.events('debugger.detached')).toEqual([{ tabId: 7, reason: 'canceled_by_user' }])
    expect((await worker.request('debugger.send', { tabId: 7, method: 'Page.enable' })).error?.message).toMatch(/^DebuggerUnavailable:/)
  })

  it('releases every attachment and overlay when Desktop clients are released', async () => {
    const worker = loadWorker([webTab(1), webTab(2)])
    await worker.request('debugger.attach', { tabId: 1 })
    await worker.request('debugger.attach', { tabId: 2 })
    await worker.request('overlay.set', { tabId: 2, active: true, sessionName: 'Research' })

    worker.sendNative({ type: 'clients-released' })
    await flush()

    expect(worker.chrome.debugger.detach.mock.calls.map(([target]) => target)).toEqual([{ tabId: 1 }, { tabId: 2 }])
    expect(worker.overlayLog.at(-1)).toBe('state:false:false')
    expect((await worker.request('debugger.send', { tabId: 1, method: 'Page.enable' })).error?.message).toMatch(/^DebuggerUnavailable:/)
  })

  it('reconnects after a disconnect through the retry timer and the alarm', async () => {
    vi.useFakeTimers()
    const worker = loadWorker([webTab(1)])
    await worker.request('debugger.attach', { tabId: 1 })

    worker.chrome.runtime.lastError = { message: 'Native host has exited.' }
    worker.ports[0]!.onDisconnect.fire()
    worker.chrome.runtime.lastError = null
    expect(worker.chrome.debugger.detach).toHaveBeenCalledWith({ tabId: 1 })
    expect(worker.chrome.alarms.create).toHaveBeenCalledWith('dotcraft-native-reconnect', { periodInMinutes: 0.5 })

    await vi.advanceTimersByTimeAsync(4999)
    expect(worker.ports).toHaveLength(1)
    await vi.advanceTimersByTimeAsync(1)
    expect(worker.ports).toHaveLength(2)
    expect(worker.chrome.alarms.clear).toHaveBeenCalledWith('dotcraft-native-reconnect')

    worker.ports[1]!.onDisconnect.fire()
    worker.chrome.alarms.onAlarm.fire({ name: 'dotcraft-native-reconnect' })
    expect(worker.ports).toHaveLength(3)
  })

  it('lists only controllable web tabs', async () => {
    const worker = loadWorker([
      webTab(1),
      webTab(2, 'chrome://extensions/'),
      webTab(3, 'file:///C:/notes.html'),
      webTab(4, 'https://chromewebstore.google.com/detail/x'),
      webTab(5, 'about:blank'),
      webTab(6, 'http://localhost:5173/')
    ])

    const result = (await worker.request('tabs.list')).result as Array<Record<string, unknown>>

    expect(result.map((tab) => tab.tabId)).toEqual([1, 3, 6])
    expect(result[0]).toEqual({ tabId: 1, windowId: 1, url: 'https://example.test/1', title: 'Tab 1', loading: false, active: true })
  })

  it('creates agent tabs in the focused window with one group per session', async () => {
    const worker = loadWorker([webTab(1)])

    const first = (await worker.request('tabs.create', { groupKey: 'thread-a', groupTitle: 'DotCraft' })).result
    await worker.request('tabs.create', { url: 'https://example.test/next', groupKey: 'thread-a', groupTitle: 'DotCraft' })
    await worker.request('tabs.create', { groupKey: 'thread-b', groupTitle: 'DotCraft' })
    await worker.request('tabs.setGroupTitle', { groupKey: 'thread-a', title: 'Research' })

    expect(first).toMatchObject({ tabId: 2, url: 'about:blank', active: false })
    expect(worker.chrome.tabs.create.mock.calls.map(([options]) => options)).toEqual([
      { url: 'about:blank', active: false, windowId: 1 },
      { url: 'https://example.test/next', active: false, windowId: 1 },
      { url: 'about:blank', active: false, windowId: 1 }
    ])
    expect(worker.chrome.tabs.group.mock.calls.map(([options]) => options)).toEqual([
      { createProperties: { windowId: 1 }, tabIds: [2] },
      { groupId: 100, tabIds: [3] },
      { createProperties: { windowId: 1 }, tabIds: [4] }
    ])
    expect(worker.chrome.tabGroups.update.mock.calls).toEqual([
      [100, { title: 'DotCraft', color: 'blue' }],
      [101, { title: 'DotCraft', color: 'blue' }],
      [100, { title: 'Research' }]
    ])
  })

  it('hides the overlay while a viewport screenshot is captured', async () => {
    const worker = loadWorker([webTab(1)])
    await worker.request('debugger.attach', { tabId: 1 })
    await worker.request('overlay.set', { tabId: 1, active: true })
    worker.chrome.debugger.sendCommand.mockImplementation(async (...args: unknown[]) => {
      worker.overlayLog.push(String(args[1]))
      return { data: 'AQID' }
    })

    await worker.request('debugger.send', { tabId: 1, method: 'Page.captureScreenshot', params: { format: 'png' } })

    expect(worker.chrome.scripting.executeScript).toHaveBeenCalledWith({ target: { tabId: 1, frameIds: [0] }, files: ['overlay.js'] })
    expect(worker.overlayLog).toEqual(['state:true:false', 'state:true:true', 'Page.captureScreenshot', 'state:true:false'])
  })

  it('reports cursor arrival once the overlay finishes moving', async () => {
    const worker = loadWorker([webTab(1), webTab(2)])
    let finishMove!: (value: unknown) => void
    await worker.request('overlay.set', { tabId: 1, active: true })
    worker.chrome.tabs.sendMessage.mockImplementationOnce(() => new Promise((resolve) => { finishMove = resolve }))

    expect((await worker.request('overlay.moveCursor', { tabId: 1, x: 10, y: 20, sequence: 3 })).result).toEqual({ ok: true, animate: true })
    await flush()
    expect(worker.events('overlay.cursorArrived')).toEqual([])
    finishMove({ ok: true })
    await flush()
    expect(worker.events('overlay.cursorArrived')).toEqual([{ tabId: 1, sequence: 3 }])

    expect((await worker.request('overlay.moveCursor', { tabId: 2, x: 1, y: 1, sequence: 4 })).result).toEqual({ ok: true, animate: false })
    await flush()
    expect(worker.events('overlay.cursorArrived')).toEqual([{ tabId: 1, sequence: 3 }, { tabId: 2, sequence: 4 }])
  })

  it('forwards tab updates and removals', async () => {
    const worker = loadWorker([webTab(1)])
    worker.chrome.tabs.onUpdated.fire(1, { status: 'loading' }, { ...webTab(1), status: 'loading' })
    worker.chrome.tabs.onUpdated.fire(1, { favIconUrl: 'x' }, webTab(1))
    worker.chrome.tabs.onRemoved.fire(1, {})

    expect(worker.events('tabs.updated')).toEqual([
      { tabId: 1, windowId: 1, url: 'https://example.test/1', title: 'Tab 1', loading: true, active: true }
    ])
    expect(worker.events('tabs.removed')).toEqual([{ tabId: 1 }])
  })

  it('republishes tab activity when the user switches tabs', async () => {
    const worker = loadWorker([webTab(1), { ...webTab(2), active: false }])
    await worker.chrome.tabs.update(1, { active: false })
    await worker.chrome.tabs.update(2, { active: true })
    worker.chrome.tabs.onActivated.fire({ tabId: 2, windowId: 1 })
    await flush()

    expect(worker.events('tabs.updated').map((tab) => ({ tabId: tab.tabId, active: tab.active }))).toEqual([
      { tabId: 1, active: false },
      { tabId: 2, active: true }
    ])
  })
})

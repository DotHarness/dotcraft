import fs from 'node:fs'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import vm from 'node:vm'
import { describe, expect, it } from 'vitest'

type ChromeTab = {
  id: number
  windowId: number
  index: number
  title?: string
  url?: string
  pendingUrl?: string
  status?: string
  active?: boolean
}

function createEvent() {
  return { addListener: () => undefined }
}

function loadServiceWorker(tabs: Map<number, ChromeTab>) {
  const nativeMessages: unknown[] = []
  const updateCalls: Array<{ tabId: number; update: Record<string, unknown> }> = []
  const createCalls: Array<Record<string, unknown>> = []
  const context = {
    console,
    setTimeout,
    clearTimeout,
    Date,
    Promise,
    Error,
    String,
    Number,
    Boolean,
    RegExp,
    Array,
    Map,
    Set,
    JSON,
    chrome: {
      runtime: {
        connectNative: () => ({
          onMessage: createEvent(),
          onDisconnect: createEvent(),
          postMessage: (message: unknown) => nativeMessages.push(message)
        }),
        getManifest: () => ({ version: '0.0.0' }),
        lastError: null,
        onInstalled: createEvent(),
        onStartup: createEvent(),
        onMessage: createEvent()
      },
      action: {
        onClicked: createEvent()
      },
      tabs: {
        async get(tabId: number) {
          const tab = tabs.get(tabId)
          if (!tab) throw new Error(`Missing tab ${tabId}`)
          return { ...tab }
        },
        async update(tabId: number, update: Record<string, unknown>) {
          updateCalls.push({ tabId, update })
          const tab = tabs.get(tabId)
          if (!tab) throw new Error(`Missing tab ${tabId}`)
          const next = { ...tab, pendingUrl: String(update.url), status: 'loading', active: update.active === true }
          tabs.set(tabId, next)
          return { ...tab }
        },
        async create(options: Record<string, unknown>) {
          createCalls.push(options)
          const id = Math.max(0, ...tabs.keys()) + 1
          const tab = {
            id,
            windowId: 1,
            index: id - 1,
            title: '',
            url: options.url ? 'about:blank' : 'about:blank',
            pendingUrl: options.url ? String(options.url) : undefined,
            status: options.url ? 'loading' : 'complete',
            active: options.active !== false
          }
          tabs.set(id, tab)
          return { ...tab }
        },
        async query() {
          return [...tabs.values()].map((tab) => ({ ...tab }))
        },
        async remove(tabId: number) {
          tabs.delete(tabId)
        },
        async reload() {
          return undefined
        },
        async captureVisibleTab() {
          return 'data:image/png;base64,AQID'
        }
      },
      debugger: {
        async attach() {
          return undefined
        },
        async detach() {
          return undefined
        },
        async sendCommand() {
          return { result: { value: null } }
        }
      }
    }
  }
  vm.createContext(context)
  const serviceWorkerPath = path.resolve(
    path.dirname(fileURLToPath(import.meta.url)),
    '../../../resources/plugins/dotcraft-bundled/plugins/chrome/extension/service_worker.js'
  )
  vm.runInContext(fs.readFileSync(serviceWorkerPath, 'utf8'), context, { filename: serviceWorkerPath })
  return {
    context: context as typeof context & {
      dispatchCommand: (method: string, params: Record<string, unknown>) => Promise<unknown>
      handleRequest: (message: Record<string, unknown>) => Promise<void>
      cancelCommand: (commandId: string, reason?: string) => boolean
    },
    updateCalls,
    createCalls,
    nativeMessages
  }
}

function sessionParams(id = 'thread-test') {
  return {
    browserSession: {
      sessionId: id,
      turnId: `turn-${id}`,
      evaluationId: `eval-${id}`,
      protocolVersion: 1
    }
  }
}

describe('chrome extension service worker', () => {
  it('waits for tab.goto URL commit and returns fresh tab state', async () => {
    const tabs = new Map<number, ChromeTab>([[
      1,
      { id: 1, windowId: 1, index: 0, title: 'Extensions', url: 'chrome://extensions/', status: 'complete', active: true }
    ]])
    let getCount = 0
    const worker = loadServiceWorker(tabs)
    const originalGet = worker.context.chrome.tabs.get
    worker.context.chrome.tabs.get = async (tabId: number) => {
      getCount += 1
      if (getCount > 1) {
        tabs.set(tabId, {
          id: tabId,
          windowId: 1,
          index: 0,
          title: 'Bilibili',
          url: 'https://www.bilibili.com/',
          status: 'complete',
          active: true
        })
      }
      return originalGet(tabId)
    }

    const result = await worker.context.dispatchCommand('tab.goto', {
      ...sessionParams(),
      tab: { id: 1 },
      url: 'https://www.bilibili.com/',
      options: { timeoutMs: 1000 }
    }) as Record<string, unknown>

    expect(worker.updateCalls).toEqual([
      { tabId: 1, update: { url: 'https://www.bilibili.com/', active: true } }
    ])
    expect(result).toMatchObject({
      id: 1,
      url: 'https://www.bilibili.com/',
      title: 'Bilibili',
      loading: false
    })
  })

  it('waits for tabs.new with a URL and honors waitUntil load', async () => {
    const tabs = new Map<number, ChromeTab>()
    const worker = loadServiceWorker(tabs)
    const originalGet = worker.context.chrome.tabs.get
    worker.context.chrome.tabs.get = async (tabId: number) => {
      const current = tabs.get(tabId)
      if (current?.pendingUrl) {
        tabs.set(tabId, {
          ...current,
          title: 'Example',
          url: current.pendingUrl,
          pendingUrl: undefined,
          status: 'complete'
        })
      }
      return originalGet(tabId)
    }

    const result = await worker.context.dispatchCommand('tabs.new', {
      ...sessionParams(),
      url: 'https://example.test/',
      active: false,
      options: { waitUntil: 'load', timeoutMs: 1000 }
    }) as Record<string, unknown>

    expect(worker.createCalls).toEqual([
      { url: 'https://example.test/', active: false }
    ])
    expect(result).toMatchObject({
      url: 'https://example.test/',
      title: 'Example',
      loading: false
    })
  })

  it('reports the current URL when navigation commit times out', async () => {
    const tabs = new Map<number, ChromeTab>([[
      1,
      { id: 1, windowId: 1, index: 0, title: 'Extensions', url: 'chrome://extensions/', status: 'complete', active: true }
    ]])
    const worker = loadServiceWorker(tabs)

    await expect(worker.context.dispatchCommand('tab.waitForNavigation', {
      ...sessionParams(),
      tab: { id: 1 },
      previousUrl: 'chrome://extensions/',
      options: { timeoutMs: 1 }
    })).rejects.toThrow('current URL is "chrome://extensions/"')
  })

  it('rejects oversized tab.evaluate results', async () => {
    const tabs = new Map<number, ChromeTab>([[
      1,
      { id: 1, windowId: 1, index: 0, title: 'Example', url: 'https://example.test/', status: 'complete', active: true }
    ]])
    const worker = loadServiceWorker(tabs)
    worker.context.chrome.debugger.sendCommand = async () => ({ result: { value: 'x'.repeat(100) } })

    await expect(worker.context.dispatchCommand('tab.evaluate', {
      ...sessionParams(),
      tab: { id: 1 },
      source: 'return "large";',
      maxBytes: 10
    })).rejects.toThrow('ResultTooLarge: tab.evaluate result exceeded 10 bytes')
  })

  it('classifies unsupported commands', async () => {
    const worker = loadServiceWorker(new Map<number, ChromeTab>())

    await expect(worker.context.dispatchCommand('dotcraft.nope', {})).rejects.toThrow(
      'SessionMetadataMissing: Chrome command requires browserSession.sessionId'
    )
    await expect(worker.context.dispatchCommand('dotcraft.nope', sessionParams())).rejects.toThrow(
      'UnsupportedApi: Unsupported DotCraft Chrome command: dotcraft.nope'
    )
  })

  it('classifies debugger attachment failures', async () => {
    const tabs = new Map<number, ChromeTab>([[
      1,
      { id: 1, windowId: 1, index: 0, title: 'Example', url: 'https://example.test/', status: 'complete', active: true }
    ]])
    const worker = loadServiceWorker(tabs)
    worker.context.chrome.debugger.attach = async () => {
      throw new Error('Another debugger is already attached')
    }

    await expect(worker.context.dispatchCommand('tab.evaluate', {
      ...sessionParams(),
      tab: { id: 1 },
      source: 'return true;'
    })).rejects.toThrow('DebuggerUnavailable: Chrome debugger bridge is unavailable')
  })

  it('isolates created and finalized tabs by browser session', async () => {
    const tabs = new Map<number, ChromeTab>()
    const worker = loadServiceWorker(tabs)
    const sessionA = sessionParams('thread-a')
    const sessionB = sessionParams('thread-b')

    const tabA = await worker.context.dispatchCommand('tabs.new', {
      ...sessionA,
      active: false
    }) as Record<string, unknown>
    const tabB = await worker.context.dispatchCommand('tabs.new', {
      ...sessionB,
      active: false
    }) as Record<string, unknown>

    const finalized = await worker.context.dispatchCommand('tabs.finalize', {
      ...sessionA,
      keep: []
    }) as Record<string, unknown>

    expect(finalized).toMatchObject({
      ok: true,
      closed: [tabA.id],
      released: []
    })
    expect(tabs.has(Number(tabA.id))).toBe(false)
    expect(tabs.has(Number(tabB.id))).toBe(true)
  })

  it('requires claimTab ids to come from the latest openTabs result for the session', async () => {
    const tabs = new Map<number, ChromeTab>([[
      1,
      { id: 1, windowId: 1, index: 0, title: 'Example', url: 'https://example.test/', status: 'complete', active: true }
    ]])
    const worker = loadServiceWorker(tabs)
    const browserSession = sessionParams('thread-claim')

    await expect(worker.context.dispatchCommand('user.claimTab', {
      ...browserSession,
      tab: { id: 1 }
    })).rejects.toThrow('latest user.openTabs')

    const openTabs = await worker.context.dispatchCommand('user.openTabs', browserSession) as Array<Record<string, unknown>>
    const claimed = await worker.context.dispatchCommand('user.claimTab', {
      ...browserSession,
      tab: openTabs[0]
    }) as Record<string, unknown>

    expect(claimed).toMatchObject({ id: 1, claimed: true })
  })

  it('cancels pending wait commands and reports CommandCancelled', async () => {
    const tabs = new Map<number, ChromeTab>([[
      1,
      { id: 1, windowId: 1, index: 0, title: 'Example', url: 'https://example.test/start', status: 'complete', active: true }
    ]])
    const worker = loadServiceWorker(tabs)
    const wait = worker.context.handleRequest({
      id: 100,
      commandId: 'cmd-wait-url',
      method: 'tab.waitForURL',
      params: {
        ...sessionParams('thread-cancel'),
        tab: { id: 1 },
        url: 'https://example.test/done',
        options: { timeoutMs: 1000 }
      }
    })

    expect(worker.context.cancelCommand('cmd-wait-url', 'outer-timeout')).toBe(true)
    await wait

    expect(worker.nativeMessages.at(-1)).toMatchObject({
      type: 'dotcraft-response',
      id: 100,
      commandId: 'cmd-wait-url',
      ok: false,
      error: expect.stringContaining('CommandCancelled: Chrome command cmd-wait-url was cancelled')
    })
  })
})

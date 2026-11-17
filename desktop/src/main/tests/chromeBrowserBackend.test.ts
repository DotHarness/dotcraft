import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'

vi.mock('electron', () => ({
  app: { getAppPath: () => process.cwd() },
  BrowserWindow: vi.fn(),
  WebContentsView: vi.fn(),
  nativeImage: { createFromBuffer: vi.fn(() => ({ isEmpty: () => true })) },
  session: { fromPartition: vi.fn() },
  shell: { openExternal: vi.fn(), openPath: vi.fn() }
}))

import { BrowserUseManager } from '../browserUseManager'
import { CHROME_BACKEND_PROFILE } from '../browserUseBackendProfile'
import { ChromeBrowserHost } from '../chrome/chromeBrowserHost'
import type { ChromeRelay } from '../chrome/chromeRelayClient'

interface FakeTab { tabId: number; url: string; title: string; loading: boolean; active: boolean; width: number; height: number }

class FakeChromeRelay extends EventEmitter implements ChromeRelay {
  readonly calls: Array<{ method: string; params: Record<string, unknown> }> = []
  readonly tabs = new Map<number, FakeTab>()
  readonly attached = new Set<number>()
  private nextTabId = 100

  constructor(userTabs: FakeTab[] = []) {
    super()
    for (const tab of userTabs) this.tabs.set(tab.tabId, tab)
  }

  count(method: string, tabId?: number): number {
    return this.calls.filter((call) => call.method === method && (tabId === undefined || call.params.tabId === tabId)).length
  }

  emitEvent(name: string, params: Record<string, unknown>): void {
    this.emit('event', name, params)
  }

  async request<T>(method: string, params: Record<string, unknown> = {}): Promise<T> {
    this.calls.push({ method, params })
    return await this.handle(method, params) as T
  }

  private tab(tabId: unknown): FakeTab {
    const tab = this.tabs.get(Number(tabId))
    if (!tab) throw new Error(`TabStale: Chrome tab ${String(tabId)} is gone.`)
    return tab
  }

  private handle(method: string, params: Record<string, unknown>): unknown {
    switch (method) {
      case 'tabs.list': return [...this.tabs.values()].map((tab) => ({ ...tab }))
      case 'tabs.create': {
        const tab = { tabId: this.nextTabId++, url: 'about:blank', title: '', loading: false, active: params.active === true, width: 1280, height: 720 }
        this.tabs.set(tab.tabId, tab)
        return { ...tab }
      }
      case 'tabs.update': {
        const tab = this.tab(params.tabId)
        if (typeof params.active === 'boolean') tab.active = params.active
        return { ...tab }
      }
      case 'tabs.close':
        this.tab(params.tabId)
        this.tabs.delete(Number(params.tabId))
        this.attached.delete(Number(params.tabId))
        queueMicrotask(() => this.emitEvent('tabs.removed', { tabId: params.tabId }))
        return { closed: true }
      case 'debugger.attach':
        this.tab(params.tabId)
        this.attached.add(Number(params.tabId))
        return { attached: true }
      case 'debugger.detach':
        this.attached.delete(Number(params.tabId))
        return { detached: true }
      case 'debugger.send':
        if (!this.attached.has(Number(params.tabId))) throw new Error('DebuggerUnavailable: tab is not attached.')
        return this.cdp(this.tab(params.tabId), String(params.method), (params.params ?? {}) as Record<string, unknown>)
      default:
        return { ok: true }
    }
  }

  private cdp(tab: FakeTab, method: string, params: Record<string, unknown>): unknown {
    const event = (name: string, eventParams: Record<string, unknown> = {}) =>
      this.emitEvent('debugger.event', { tabId: tab.tabId, method: name, params: eventParams })
    switch (method) {
      case 'Page.getFrameTree': return { frameTree: { frame: { id: 'main', url: tab.url } } }
      case 'Page.getNavigationHistory': return { currentIndex: 0, entries: [{ id: 1 }] }
      case 'Page.navigate': {
        const url = String(params.url)
        if (url.includes('unreachable')) return { errorText: 'net::ERR_NAME_NOT_RESOLVED' }
        event('Page.frameStartedLoading', { frameId: 'main' })
        setTimeout(() => {
          tab.url = url
          event('Page.frameNavigated', { frame: { id: 'main', url } })
          event('Page.domContentEventFired')
          event('Page.loadEventFired')
          event('Page.frameStoppedLoading', { frameId: 'main' })
        }, 0)
        return { frameId: 'main', loaderId: 'loader' }
      }
      case 'Runtime.evaluate': {
        const expression = String(params.expression ?? '')
        if (expression.includes('readyState')) {
          return { result: { value: { url: tab.url, title: tab.title, readyState: 'complete', hasBody: true, bodyTextLength: 1, interactiveCount: 1, appRootTextLength: 1 } } }
        }
        return { result: { value: 'ok' } }
      }
      default: return {}
    }
  }
}

function createOwner() {
  const emitter = new EventEmitter()
  return {
    on: emitter.on.bind(emitter),
    once: emitter.once.bind(emitter),
    off: emitter.off.bind(emitter),
    getTitle: () => 'test-window',
    isDestroyed: () => false,
    webContents: { id: 1, isDestroyed: () => false, send: vi.fn() }
  } as unknown as Electron.BrowserWindow & { webContents: { send: ReturnType<typeof vi.fn> } }
}

const managers: BrowserUseManager[] = []

afterEach(async () => {
  await Promise.all(managers.splice(0).map((manager) => manager.closeBackendForTests()))
})

async function openChromeSession(relay: FakeChromeRelay, threadId = 'chrome-thread') {
  const manager = new BrowserUseManager(new ChromeBrowserHost(relay), {}, CHROME_BACKEND_PROFILE)
  managers.push(manager)
  const owner = createOwner()
  await manager.prepareNodeRepl(owner, {
    threadId,
    evaluationId: 'eval-1',
    browserSession: { sessionId: threadId, turnId: 'turn-1' }
  })
  const call = (method: string, params: Record<string, unknown> = {}) =>
    manager.handleBrowserUseBackendRequest(method, { session_id: threadId, turn_id: 'turn-1', ...params })
  const evaluate = (tab: { id: number }) =>
    call('executeCdp', { target: { tabId: tab.id }, method: 'Runtime.evaluate', commandParams: { expression: '1 + 1', returnByValue: true } })
  return { manager, owner, call, evaluate }
}

describe('Chrome browser backend', () => {
  it('identifies itself as the extension backend without the in-app visibility capability', async () => {
    const { call } = await openChromeSession(new FakeChromeRelay())

    const info = await call('getInfo') as { id: string; type: string; capabilities: { browser: Array<{ id: string }> } }

    expect(info).toMatchObject({ id: 'extension', type: 'extension', name: 'Chrome' })
    expect(info.capabilities.browser.map((capability) => capability.id)).toEqual(['viewport'])
  })

  it('keeps one debugger attachment across commands and closes agent tabs at finalize', async () => {
    const relay = new FakeChromeRelay()
    const { call, evaluate, owner } = await openChromeSession(relay)

    const tab = await call('createTab', { url: 'https://example.com/' }) as { id: number; url: string }
    await call('createTab')
    for (let index = 0; index < 3; index += 1) await evaluate(tab)
    const created = relay.calls.filter((item) => item.method === 'tabs.create').map((item) => item.params.active)
    const chromeTabId = relay.calls.find((item) => item.method === 'debugger.attach')?.params.tabId as number
    const finalized = await call('finalizeTabs', { keep: [] }) as { closed: number[] }

    expect(tab.url).toBe('https://example.com/')
    expect(created).toEqual([true, false])
    expect(relay.count('debugger.attach', chromeTabId)).toBe(1)
    expect(relay.count('tabs.close', chromeTabId)).toBe(1)
    expect(finalized.closed).toHaveLength(2)
    expect(owner.webContents.send).not.toHaveBeenCalled()
  })

  it('controls user tabs only after a claim and releases them when the turn ends', async () => {
    const relay = new FakeChromeRelay([{ tabId: 50, url: 'https://mail.example/', title: 'Mail', loading: false, active: true, width: 1280, height: 720 }])
    const { manager, call, evaluate } = await openChromeSession(relay)

    const userTabs = await call('getUserTabs') as Array<{ id: number; url: string }>
    const before = await call('getTabs') as unknown[]
    await call('claimUserTab', { tabId: userTabs[0].id })
    const after = await call('getTabs') as Array<{ id: number }>
    await evaluate(userTabs[0])
    manager.handleTurnNotification('turn/completed', { threadId: 'chrome-thread', turn: { id: 'turn-1' } })

    expect(userTabs).toEqual([expect.objectContaining({ url: 'https://mail.example/' })])
    expect(before).toEqual([])
    expect(after.map((tab) => tab.id)).toEqual([userTabs[0].id])
    expect(relay.count('debugger.detach', 50)).toBe(1)
    expect(relay.calls).toContainEqual({ method: 'overlay.set', params: { tabId: 50, active: false } })
    expect(relay.count('tabs.close', 50)).toBe(0)
  })

  it('stops controlling a tab after the user cancels debugging in Chrome', async () => {
    const relay = new FakeChromeRelay()
    const { call, evaluate } = await openChromeSession(relay)
    const tab = await call('createTab', { url: 'https://example.com/' }) as { id: number }
    const chromeTabId = relay.calls.find((item) => item.method === 'debugger.attach')?.params.tabId

    relay.emitEvent('debugger.detached', { tabId: chromeTabId, reason: 'canceled_by_user' })

    await expect(evaluate(tab)).rejects.toThrow(/^CommandCancelled: Browser control was stopped in Chrome/)
    expect(relay.count('debugger.attach')).toBe(1)
  })

  it('reports Chrome navigation errors as navigation failures', async () => {
    const { call } = await openChromeSession(new FakeChromeRelay())

    await expect(call('createTab', { url: 'https://unreachable.example/' }))
      .rejects.toThrow(/^NavigationFailed: net::ERR_NAME_NOT_RESOLVED/)
  })
})

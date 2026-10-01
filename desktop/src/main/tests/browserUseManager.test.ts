import { afterEach, describe, expect, it, vi } from 'vitest'
import { EventEmitter } from 'events'

vi.mock('electron', () => ({
  app: { getAppPath: () => process.cwd() },
  BrowserWindow: vi.fn(),
  WebContentsView: vi.fn(),
  nativeImage: { createFromBuffer: vi.fn(() => ({ isEmpty: () => true })) },
  session: { fromPartition: vi.fn() },
  shell: { openExternal: vi.fn(), openPath: vi.fn() }
}))

import { BrowserWindow } from 'electron'
import {
  BrowserUseManager,
  isBrowserUseUrlAllowed,
  normalizeBrowserUseUrl
} from '../browserUseManager'
import { resolveBrowserUseNavigationDecision } from '../browserUsePolicy'

const activeManagers = new Set<BrowserUseManager>()

afterEach(async () => {
  await Promise.all([...activeManagers].map((manager) => manager.closeBackendForTests()))
  activeManagers.clear()
})

function isReadinessProbe(script: string): boolean {
  return script.includes('readyState') &&
    script.includes('bodyTextLength') &&
    script.includes('interactiveCount') &&
    script.includes('appRootTextLength')
}

function createFakeWebContents() {
  const emitter = new EventEmitter()
  const debuggerEmitter = new EventEmitter()
  let url = 'about:blank'
  let debuggerAttached = false
  const api = {
    ...emitter,
    on: emitter.on.bind(emitter),
    once: emitter.once.bind(emitter),
    off: emitter.off.bind(emitter),
    removeListener: emitter.removeListener.bind(emitter),
    emit: emitter.emit.bind(emitter),
    isDestroyed: vi.fn(() => false),
    getURL: vi.fn(() => url),
    getBackgroundThrottling: vi.fn(() => false),
    getTitle: vi.fn(() => 'Test Page'),
    isLoading: vi.fn(() => false),
    loadURL: vi.fn(async (nextUrl: string) => {
      url = nextUrl
    }),
    executeJavaScript: vi.fn(async (script: string) => {
      if (script.includes('document.documentElement ? document.documentElement.outerHTML')) {
        return '<html><body><button>Save</button><a href="/test">Test Link</a></body></html>'
      }
      if (script.includes('document.body ? document.body.innerText')) {
        return 'Save\nTest Link'
      }
      if (script.includes('__dotcraftBrowserUseSnapshot &&')) return false
      if (script.includes('module.exports.InjectedScript')) return true
      if (isReadinessProbe(script)) {
        return {
          url,
          title: 'Test Page',
          readyState: 'complete',
          hasBody: true,
          bodyTextLength: url === 'about:blank' ? 0 : 12,
          interactiveCount: url === 'about:blank' ? 0 : 1,
          appRootTextLength: url === 'about:blank' ? 0 : 12
        }
      }
      if (script.includes('__dotcraftBrowserUseSnapshot')) {
        return {
          title: 'Test Page',
          url,
          bodyText: url === 'about:blank' ? '' : 'Test Page',
          elements: url === 'about:blank'
            ? []
            : [{
                index: 0,
                tagName: 'a',
                tag: 'a',
                role: 'link',
                name: 'Test Link',
                text: 'Test Link',
                href: '/test',
                selector: 'a[href="/test"]',
                visible: true,
                enabled: true,
                visibleText: 'Test Link',
                ariaName: 'Test Link',
                boundingBox: { x: 10, y: 20, width: 100, height: 40 }
              }]
        }
      }
      return 'ok'
    }),
    capturePage: vi.fn(async () => ({ toPNG: () => Buffer.from([1, 2, 3]) })),
    insertText: vi.fn(),
    sendInputEvent: vi.fn(),
    debugger: {
      on: debuggerEmitter.on.bind(debuggerEmitter),
      once: debuggerEmitter.once.bind(debuggerEmitter),
      off: debuggerEmitter.off.bind(debuggerEmitter),
      emit: debuggerEmitter.emit.bind(debuggerEmitter),
      isAttached: vi.fn(() => debuggerAttached),
      attach: vi.fn(() => {
        debuggerAttached = true
      }),
      detach: vi.fn(() => {
        debuggerAttached = false
        debuggerEmitter.emit('detach', {}, 'target closed')
      }),
      sendCommand: vi.fn(async (method: string, params?: Record<string, unknown>) => {
        if (method === 'Runtime.evaluate') {
          return { result: { value: await api.executeJavaScript(String(params?.expression ?? '')) } }
        }
        if (method === 'Page.getLayoutMetrics') {
          return {
            cssContentSize: { x: 0, y: 0, width: 1280, height: 720 },
            cssVisualViewport: { pageX: 0, pageY: 0, clientWidth: 1280, clientHeight: 720 },
            contentSize: { x: 0, y: 0, width: 1280, height: 720 }
          }
        }
        if (method === 'Page.navigate') {
          url = String(params?.url ?? url)
          debuggerEmitter.emit('message', {}, 'Page.frameNavigated', { frame: { id: 'main-frame', url } })
          debuggerEmitter.emit('message', {}, 'Page.domContentEventFired', { timestamp: Date.now() / 1000 })
          debuggerEmitter.emit('message', {}, 'Page.loadEventFired', { timestamp: Date.now() / 1000 })
          return { frameId: 'main' }
        }
        if (method === 'Page.startScreencast') {
          debuggerEmitter.emit('message', {}, 'Page.screencastVisibilityChanged', { visible: false })
          return {}
        }
        if (method === 'Page.captureScreenshot') {
          return { data: 'AQID' }
        }
        return {}
      })
    },
    setUrl(nextUrl: string) {
      url = nextUrl
    }
  }
  return api as unknown as Electron.WebContents & { setUrl(nextUrl: string): void }
}

function createFakeHost(webContents = createFakeWebContents()) {
  const viewports = new Map<string, { width: number; height: number }>()
  return {
    setCaptureSurface: vi.fn(),
    createAutomationTab: vi.fn(),
    getTabWebContents: vi.fn(() => webContents),
    getAutomationTargetTab: vi.fn((): { tabId: string; currentUrl: string; title: string; loading: boolean } | null => null),
    loadAutomationUrl: vi.fn(async (_win: Electron.BrowserWindow, params: { tabId: string; url: string }) => {
      webContents.setUrl(params.url)
    }),
    destroyTab: vi.fn(),
    snapshotState: vi.fn((_win: Electron.BrowserWindow, tabId: string) => ({
      tabId,
      currentUrl: webContents.getURL(),
      title: webContents.getTitle(),
      loading: webContents.isLoading()
    })),
    setAutomationState: vi.fn(),
    setViewport: vi.fn((_win: Electron.BrowserWindow, params: { tabId: string; viewport?: { width: number; height: number } }) => {
      if (params.viewport) viewports.set(params.tabId, params.viewport)
      else viewports.delete(params.tabId)
    }),
    getLayoutSize: vi.fn((_win: Electron.BrowserWindow, tabId: string) => viewports.get(tabId) ?? { width: 1280, height: 720 }),
    isVisible: vi.fn((_win: Electron.BrowserWindow, _tabId: string) => false),
    setVisible: vi.fn(),
    moveMouse: vi.fn()
  }
}

function createFakeOwner() {
  const emitter = new EventEmitter()
  return {
    on: emitter.on.bind(emitter),
    once: emitter.once.bind(emitter),
    off: emitter.off.bind(emitter),
    getTitle: () => 'test-window',
    isDestroyed: () => false,
    webContents: {
      isDestroyed: () => false,
      send: vi.fn()
    }
  } as unknown as Electron.BrowserWindow & { webContents: { send: ReturnType<typeof vi.fn> } }
}

async function openSession(
  manager: BrowserUseManager,
  owner: Electron.BrowserWindow,
  threadId: string,
  workspacePath?: string
) {
  activeManagers.add(manager)
  const runtime = await manager.prepareNodeRepl(owner as BrowserWindow, {
    threadId,
    workspacePath,
    evaluationId: 'eval-1',
    browserSession: { sessionId: threadId, turnId: 'turn-1' }
  })
  const session = { session_id: threadId, turn_id: 'turn-1' }
  const call = (method: string, params: Record<string, unknown> = {}) =>
    manager.handleBrowserUseBackendRequest(method, { ...session, ...params })
  const command = (type: string, params: Record<string, unknown> = {}) =>
    call('executeUnhandledCommand', { type, ...params }) as Promise<Record<string, unknown>>
  const createTab = async (url?: string, params: Record<string, unknown> = {}) =>
    await call('createTab', { ...(url ? { url } : {}), ...params }) as { id: number; url: string }
  const navigate = (tab: { id: number }, url: string) =>
    call('executeCdp', { target: { tabId: tab.id }, method: 'Page.navigate', commandParams: { url } })
  const domSnapshot = async (tab: { id: number }, params: Record<string, unknown> = {}) =>
    String((await command('playwright_dom_snapshot', { tab_id: tab.id, ...params })).dom_snapshot)
  return { call, command, createTab, navigate, domSnapshot, collect: runtime.collect }
}

describe('normalizeBrowserUseUrl', () => {
  it('defaults local host-like URLs to http', () => {
    expect(normalizeBrowserUseUrl('localhost:3000')).toBe('http://localhost:3000/')
    expect(normalizeBrowserUseUrl('127.0.0.1:5173/app')).toBe('http://127.0.0.1:5173/app')
  })

  it('normalizes absolute URLs and rejects invalid input', () => {
    expect(normalizeBrowserUseUrl('http://localhost:3000')).toBe('http://localhost:3000/')
    expect(normalizeBrowserUseUrl('\u0000http://localhost')).toBeNull()
  })
})

describe('isBrowserUseUrlAllowed', () => {
  it('allows local, file, and dotcraft-viewer URLs', () => {
    expect(isBrowserUseUrlAllowed('http://localhost:3000/')).toBe(true)
    expect(isBrowserUseUrlAllowed('https://127.0.0.1:8443/')).toBe(true)
    expect(isBrowserUseUrlAllowed('file:///tmp/index.html')).toBe(true)
    expect(isBrowserUseUrlAllowed('dotcraft-viewer://workspace/E%3A/index.html')).toBe(true)
  })

  it('blocks remote and unsupported URLs', () => {
    expect(isBrowserUseUrlAllowed('https://example.com/')).toBe(false)
    expect(isBrowserUseUrlAllowed('javascript:alert(1)')).toBe(false)
  })
})

describe('browser navigation policy', () => {
  it('allows configured external domains and their subdomains', () => {
    expect(resolveBrowserUseNavigationDecision('https://example.com/', {
      approvalMode: 'alwaysAsk',
      allowedDomains: ['example.com']
    })).toEqual({ kind: 'allow', local: false, domain: 'example.com' })
    expect(resolveBrowserUseNavigationDecision('https://docs.example.com/', {
      approvalMode: 'alwaysAsk',
      allowedDomains: ['example.com']
    })).toEqual({ kind: 'allow', local: false, domain: 'docs.example.com' })
  })

  it('lets blocked domains override allowed domains', () => {
    expect(resolveBrowserUseNavigationDecision('https://docs.example.com/', {
      approvalMode: 'neverAsk',
      allowedDomains: ['example.com'],
      blockedDomains: ['docs.example.com']
    })).toMatchObject({ kind: 'block', domain: 'docs.example.com' })
  })

  it('requires approval for unknown external domains by default', () => {
    expect(resolveBrowserUseNavigationDecision('https://example.com/')).toEqual({
      kind: 'needs-approval',
      domain: 'example.com'
    })
  })

  it('allows unknown external domains when approval is disabled', () => {
    expect(resolveBrowserUseNavigationDecision('https://example.com/', {
      approvalMode: 'neverAsk'
    })).toEqual({ kind: 'allow', local: false, domain: 'example.com' })
  })
})

describe('BrowserUseManager IAB backend', () => {
  it('opens tabs through viewer browser in the background by default', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { call, createTab } = await openSession(manager, owner, 'thread-1', '/workspace/test-root')

    await call('nameSession', { name: 'mario-test' })
    const tab = await createTab('localhost:3000')

    expect(tab.url).toBe('http://localhost:3000/')
    expect(host.createAutomationTab).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: expect.stringMatching(/^browser-thread-1-/),
      workspacePath: '/workspace/test-root',
      allowFileScheme: true
    }))
    const createdTabId = host.createAutomationTab.mock.calls[0]?.[1]?.tabId
    expect(host.setVisible).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: createdTabId,
      visible: false
    }))
    expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:open', expect.objectContaining({
      threadId: 'thread-1',
      initialUrl: 'http://localhost:3000/',
      title: 'mario-test',
      focusMode: 'none'
    }))
    expect(BrowserWindow).not.toHaveBeenCalled()
  })

  it('focuses the first tab when visibility is requested before opening', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-1')

    await command('browser_visibility_set', { visible: true })
    await createTab('localhost:3000')

    expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:open', expect.objectContaining({
      focusMode: 'first-open'
    }))
  })

  it('creates a stable blank tab before taking the first DOM snapshot', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (isReadinessProbe(script)) {
        return {
          url: 'about:blank',
          title: 'Test Page',
          readyState: 'complete',
          bodyTextLength: 0,
          interactiveCount: 0,
          appRootTextLength: 0
        }
      }
      if (script.includes('__dotcraftBrowserUseSnapshot')) {
        return {
          title: 'Test Page',
          url: 'about:blank',
          bodyText: '',
          elements: []
        }
      }
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { createTab, domSnapshot } = await openSession(manager, owner, 'thread-blank', '/workspace/test-root')

    const tab = await createTab()

    expect(JSON.parse(await domSnapshot(tab))).toMatchObject({
      title: 'Test Page',
      url: 'about:blank'
    })
    expect(host.createAutomationTab).toHaveBeenCalledWith(owner, expect.objectContaining({
      initialUrl: 'about:blank'
    }))
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, expect.objectContaining({
      url: 'about:blank'
    }))
    expect(wc.executeJavaScript).toHaveBeenCalled()
  })

  it('returns DOM snapshots for ready documents with empty body text', async () => {
    const wc = createFakeWebContents()
    const defaultExecuteJavaScript = (wc.executeJavaScript as ReturnType<typeof vi.fn>).getMockImplementation()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (isReadinessProbe(script)) {
        return {
          url: 'http://localhost:3000/empty',
          title: '',
          readyState: 'complete',
          hasBody: true,
          bodyTextLength: 0,
          interactiveCount: 0,
          appRootTextLength: 0
        }
      }
      if (script.includes('__dotcraftBrowserUseSnapshot')) {
        return {
          title: '',
          url: 'http://localhost:3000/empty',
          bodyText: '',
          elements: []
        }
      }
      return defaultExecuteJavaScript?.(script) ?? 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { createTab, domSnapshot } = await openSession(manager, owner, 'thread-empty-ready-snapshot')

    const snapshotText = await domSnapshot(await createTab('localhost:3000/empty'))

    expect(snapshotText).toContain('"elements": []')
    expect(snapshotText.indexOf('"title"')).toBeLessThan(snapshotText.indexOf('"url"'))
    expect(snapshotText.indexOf('"url"')).toBeLessThan(snapshotText.indexOf('"bodyText"'))
    expect(snapshotText.indexOf('"bodyText"')).toBeLessThan(snapshotText.indexOf('"accessibilitySnapshot"'))
    expect(snapshotText.indexOf('"accessibilitySnapshot"')).toBeLessThan(snapshotText.indexOf('"elements"'))
  })

  it('returns a readable timeout when page JavaScript evaluation hangs', async () => {
    const wc = createFakeWebContents()
    let releaseScript: (() => void) | undefined
    let scriptPromise: Promise<unknown> | undefined
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(() => new Promise((resolve) => {
      scriptPromise = new Promise((innerResolve) => {
        releaseScript = () => {
          resolve('late')
          innerResolve('late')
        }
      })
    }))
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host, { operationMs: 25 })
    const owner = createFakeOwner()
    const { createTab, domSnapshot } = await openSession(manager, owner, 'thread-timeout', '/workspace/test-root')
    const tab = await createTab(undefined, { timeoutMs: 5_000 })

    const error = await domSnapshot(tab, { timeoutMs: 5_000 }).catch((reason: Error) => reason)

    expect((error as Error).message).toContain("Browser operation 'domSnapshot.ready' timed out")
    expect((error as Error).message).toContain('browser-thread-timeout-')
    expect((error as Error).message).toContain('about:blank')
    releaseScript?.()
    await scriptPromise
  }, 15_000)

  it('opens 127.0.0.1 dev server URLs through the viewer host', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { createTab } = await openSession(manager, owner, 'thread-1', '/workspace/test-root')

    const tab = await createTab('127.0.0.1:5173')

    expect(tab.url).toBe('http://127.0.0.1:5173/')
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, {
      tabId: expect.stringMatching(/^browser-thread-1-/),
      url: 'http://127.0.0.1:5173/'
    })
  })

  it('waits for VitePress-like content before returning a DOM snapshot', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (isReadinessProbe(script)) {
        return {
          url: 'http://127.0.0.1:5173/',
          title: 'DotCraft',
          readyState: 'complete',
          bodyTextLength: 46,
          interactiveCount: 3,
          appRootTextLength: 46
        }
      }
      if (script.includes('__dotcraftBrowserUseSnapshot')) {
        return {
          title: 'DotCraft',
          url: 'http://127.0.0.1:5173/',
          bodyText: 'DotCraft Search Guide Blog',
          elements: ['a "/" "Guide"', 'button "Search"', 'a "/blog/" "Blog"']
        }
      }
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { command, createTab, domSnapshot } = await openSession(manager, owner, 'thread-vitepress', '/workspace/test-root')
    const tab = await createTab('http://127.0.0.1:5173/')

    await command('playwright_wait_for_load_state', { tab_id: tab.id, state: 'load' })

    expect(JSON.parse(await domSnapshot(tab))).toMatchObject({
      title: 'DotCraft',
      bodyText: expect.stringContaining('Search')
    })
  })

  it('supports networkidle load state without hanging', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { call, command, createTab } = await openSession(manager, owner, 'thread-networkidle', '/workspace/test-root')
    const tab = await createTab('http://127.0.0.1:5173/')

    await command('playwright_wait_for_load_state', { tab_id: tab.id, state: 'networkidle', timeout_ms: 2_000 })

    expect(await call('getTabs')).toMatchObject([{ url: 'http://127.0.0.1:5173/' }])
  })

  it('treats already-ready DOMContentLoaded documents as loaded without requestAnimationFrame', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('requestAnimationFrame')) {
        throw new Error('readiness probes must not depend on requestAnimationFrame')
      }
      if (isReadinessProbe(script)) {
        return {
          url: 'http://127.0.0.1:5173/background',
          title: '',
          readyState: 'interactive',
          hasBody: true,
          bodyTextLength: 0,
          interactiveCount: 0,
          appRootTextLength: 0
        }
      }
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-domcontentloaded-ready', '/workspace/test-root')
    const tab = await createTab('http://127.0.0.1:5173/background')

    await command('playwright_wait_for_load_state', { tab_id: tab.id, state: 'domcontentloaded', timeout_ms: 1_000 })

    expect((wc.executeJavaScript as ReturnType<typeof vi.fn>).mock.calls.some(([script]) => String(script).includes('requestAnimationFrame'))).toBe(false)
  })

  it('rejects wait for load state on main-frame navigation failures', async () => {
    const wc = createFakeWebContents()
    let loading = false
    ;(wc.isLoading as ReturnType<typeof vi.fn>).mockImplementation(() => loading)
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-failed-load', '/workspace/test-root')
    const tab = await createTab('http://127.0.0.1:5173/missing')

    loading = true
    const pending = command('playwright_wait_for_load_state', { tab_id: tab.id, state: 'load', timeout_ms: 1_000 })
    await new Promise((resolve) => setTimeout(resolve, 25))
    loading = false
    ;(wc as unknown as EventEmitter).emit(
      'did-fail-load',
      {},
      -105,
      'ERR_NAME_NOT_RESOLVED',
      'http://127.0.0.1:5173/missing',
      true
    )

    await expect(pending).rejects.toThrow('NavigationFailed: ERR_NAME_NOT_RESOLVED')
  })

  it('does not report a failed initial navigation URL as the loaded tab URL', async () => {
    const wc = createFakeWebContents()
    const host = createFakeHost(wc)
    host.loadAutomationUrl.mockImplementation(async () => {
      throw new Error('NavigationFailed: ERR_CONNECTION_CLOSED')
    })
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { call, createTab } = await openSession(manager, owner, 'thread-failed-initial-url', '/workspace/test-root')

    await expect(createTab('127.0.0.1:5173/missing')).rejects.toThrow('NavigationFailed: ERR_CONNECTION_CLOSED')

    const tabs = await call('getTabs') as Array<Record<string, unknown>>
    expect(tabs).toHaveLength(1)
    expect(tabs[0]!.url).toBe('about:blank')
  })

  it('returns a readable timeout when screenshot capture hangs', async () => {
    const wc = createFakeWebContents()
    let releaseCapture: (() => void) | undefined
    const defaultSendCommand = (wc.debugger.sendCommand as ReturnType<typeof vi.fn>).getMockImplementation()
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(
      async (method: string, params?: Record<string, unknown>) => {
        if (method === 'Page.captureScreenshot') {
          return await new Promise((resolve) => {
            releaseCapture = () => resolve({ data: 'CQkJ' })
          })
        }
        return await defaultSendCommand?.(method, params)
      })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host, { operationMs: 25 })
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-shot-timeout', '/workspace/test-root')
    const tab = await createTab('http://127.0.0.1:5173/', { timeoutMs: 5_000 })

    const error = await command('tab_screenshot', { tab_id: tab.id, timeoutMs: 5_000 }).catch((reason: Error) => reason)

    expect((error as Error).message).toContain("Browser operation 'screenshot' timed out")
    expect((error as Error).message).toContain('http://127.0.0.1:5173/')
    releaseCapture?.()
  })

  it('captures viewport screenshots through CDP', async () => {
    const wc = createFakeWebContents()
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-viewport-shot', '/workspace/test-root')
    const tab = await createTab('http://127.0.0.1:5173/')

    expect(await command('tab_screenshot', { tab_id: tab.id })).toEqual({ data: 'AQID' })

    expect(wc.capturePage).not.toHaveBeenCalled()
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Page.captureScreenshot', {
      format: 'jpeg',
      quality: 80,
      clip: {
        x: 0,
        y: 0,
        width: 1280,
        height: 720,
        scale: 1
      }
    })
  })

  it('captures a visible tab in place and a hidden tab on a surface at its layout size', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const capture = async (threadId: string) => {
      const { command, createTab } = await openSession(manager, owner, threadId, '/workspace/test-root')
      const tab = await createTab('http://127.0.0.1:5173/')
      await command('tab_screenshot', { tab_id: tab.id })
    }

    host.isVisible.mockReturnValue(true)
    await capture('thread-visible-shot')
    expect(host.setCaptureSurface).not.toHaveBeenCalled()

    host.isVisible.mockReturnValue(false)
    await capture('thread-hidden-shot')
    expect(host.setCaptureSurface.mock.calls.map(call => call[2])).toEqual([{ width: 1280, height: 720 }, null])
  })

  it('sizes the screenshot capture surface from the tab explicit viewport', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-viewport-surface', '/workspace/test-root')
    const tab = await createTab('http://127.0.0.1:5173/')

    await command('browser_viewport_set', { width: 800, height: 600 })
    await command('tab_screenshot', { tab_id: tab.id })

    expect(host.setCaptureSurface).toHaveBeenCalledWith(owner, expect.any(String), { width: 800, height: 600 })
  })

  it('rejects empty CDP screenshot data', async () => {
    const wc = createFakeWebContents()
    const defaultSendCommand = (wc.debugger.sendCommand as ReturnType<typeof vi.fn>).getMockImplementation()
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(
      async (method: string, params?: Record<string, unknown>) => {
        if (method === 'Page.captureScreenshot') return { data: '' }
        return await defaultSendCommand?.(method, params)
      })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-empty-shot', '/workspace/test-root')
    const tab = await createTab('http://127.0.0.1:5173/')

    await expect(command('tab_screenshot', { tab_id: tab.id })).rejects.toThrow('Page.captureScreenshot returned no data')
  })

  it('captures full-page screenshots with the CDP page dimensions', async () => {
    const wc = createFakeWebContents()
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'Runtime.evaluate') {
        const value = await wc.executeJavaScript(String(params?.expression ?? ''))
        return { result: { value } }
      }
      if (method === 'Page.getLayoutMetrics') {
        return { cssContentSize: { x: 0, y: 0, width: 1280, height: 2400 }, cssVisualViewport: { pageX: 0, pageY: 0, clientWidth: 1280, clientHeight: 2400 } }
      }
      if (method === 'Page.captureScreenshot') {
        return { data: 'CQgH' }
      }
      return {}
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-full-page-shot', '/workspace/test-root')
    const tab = await createTab('http://127.0.0.1:5173/')

    expect(await command('tab_screenshot', { tab_id: tab.id, fullPage: true })).toEqual({ data: 'CQgH' })

    expect(wc.capturePage).not.toHaveBeenCalled()
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Page.captureScreenshot', {
      format: 'jpeg',
      quality: 80,
      captureBeyondViewport: true,
      clip: { x: 0, y: 0, width: 1280, height: 2400, scale: 1 }
    })
    expect(host.setCaptureSurface.mock.calls.map(call => call[2])).toEqual([{ width: 1280, height: 2400 }, null])
  })

  it('captures a cropped screenshot as a CSS-pixel JPEG clip', async () => {
    const wc = createFakeWebContents()
    const defaultSendCommand = (wc.debugger.sendCommand as ReturnType<typeof vi.fn>).getMockImplementation()
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(
      async (method: string, params?: Record<string, unknown>) => {
        if (method === 'Runtime.evaluate' && params?.expression === 'window.devicePixelRatio') return { result: { value: 2 } }
        return await defaultSendCommand?.(method, params)
      })
    const manager = new BrowserUseManager(createFakeHost(wc))
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-crop-shot')
    const tab = await createTab('localhost:3000')

    expect(await command('tab_screenshot', {
      tab_id: tab.id, cropX: 10, cropY: 20, cropWidth: 100, cropHeight: 40
    })).toEqual({ data: 'AQID' })

    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Page.captureScreenshot', {
      format: 'jpeg',
      quality: 80,
      captureBeyondViewport: true,
      clip: { x: 10, y: 20, width: 100, height: 40, scale: 0.5 }
    })
  })

  it('includes browser operation diagnostics when page JavaScript times out', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(() => new Promise(() => {}))
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host, { operationMs: 25 })
    const owner = createFakeOwner()
    const { collect, createTab, domSnapshot } = await openSession(manager, owner, 'thread-diag-timeout', '/workspace/test-root')
    const tab = await createTab(undefined, { timeoutMs: 5_000 })

    await expect(domSnapshot(tab, { timeoutMs: 5_000 })).rejects.toThrow("Browser operation 'domSnapshot.ready' timed out")

    expect(collect().logs.join('\n')).toContain('Recent browser operations')
    expect(collect().logs.join('\n')).toContain('domSnapshot.ready')
  })

  it('does not force focus for background tabs in the same thread', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { createTab } = await openSession(manager, owner, 'thread-1')

    await createTab('localhost:3000')
    await createTab('localhost:3001')

    expect(owner.webContents.send).toHaveBeenNthCalledWith(1, 'viewer:browser:open', expect.objectContaining({
      focusMode: 'none'
    }))
    expect(owner.webContents.send).toHaveBeenNthCalledWith(2, 'viewer:browser:open', expect.objectContaining({
      focusMode: 'none'
    }))
  })

  it('reset leaves claimed user browser tabs open but clears automation state', async () => {
    const host = createFakeHost()
    host.getAutomationTargetTab.mockReturnValue({
      tabId: 'user-browser-tab',
      currentUrl: 'about:blank',
      title: 'User tab',
      loading: false
    })
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { call } = await openSession(manager, owner, 'thread-1', '/workspace/test-root')
    const [listed] = await call('getUserTabs') as Array<{ id: number }>
    await call('claimUserTab', { tabId: listed!.id })

    expect(manager.reset('thread-1')).toEqual({ ok: true })

    expect(host.destroyTab).not.toHaveBeenCalled()
    expect(host.setAutomationState).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: 'user-browser-tab',
      active: false,
      release: true
    }))
  })

  it('reset destroys viewer browser tabs for the thread', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { createTab } = await openSession(manager, owner, 'thread-1')
    await createTab('localhost:3000')

    expect(manager.reset('thread-1')).toEqual({ ok: true })
    expect(host.destroyTab).toHaveBeenCalledWith(owner, expect.stringMatching(/^browser-thread-1-/))
    expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:close', {
      threadId: 'thread-1',
      tabId: expect.stringMatching(/^browser-thread-1-/)
    })
  })

  it('opens external URLs when approval is disabled', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    manager.setPolicyHost({
      getSettings: () => ({ browserUse: { approvalMode: 'neverAsk' } }),
      updateSettings: vi.fn()
    })
    const { createTab } = await openSession(manager, owner, 'thread-1', '/workspace/test-root')

    const tab = await createTab('https://example.com')

    expect(tab.url).toBe('https://example.com/')
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, expect.objectContaining({
      url: 'https://example.com/'
    }))
  })

  it('blocks configured external domains before loading', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    manager.setPolicyHost({
      getSettings: () => ({ browserUse: { blockedDomains: ['example.com'] } }),
      updateSettings: vi.fn()
    })
    const { createTab } = await openSession(manager, owner, 'thread-1', '/workspace/test-root')

    await expect(createTab('https://example.com')).rejects.toThrow('Blocked browser domain: example.com')

    expect(host.loadAutomationUrl).not.toHaveBeenCalled()
  })

  it('persists allow-domain approval and continues navigation', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const settings = { browserUse: { approvalMode: 'alwaysAsk' as const, allowedDomains: [] as string[] } }
    manager.setPolicyHost({
      getSettings: () => settings,
      updateSettings: vi.fn(async (partial) => {
        Object.assign(settings, partial)
      })
    })
    const { createTab } = await openSession(manager, owner, 'thread-1', '/workspace/test-root')

    const pending = createTab('https://example.com')

    await vi.waitFor(() => {
      expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:approval-request', expect.objectContaining({
        domain: 'example.com'
      }))
    })
    const payload = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls[0][1] as { requestId: string }
    expect(manager.handleApprovalResponse({ requestId: payload.requestId, action: 'allowDomain' })).toBe(true)

    await pending
    expect(settings.browserUse.allowedDomains).toEqual(['example.com'])
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, expect.objectContaining({
      url: 'https://example.com/'
    }))
  })

  it('uses allow-once approval for initial URL without prompting twice', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const updateSettings = vi.fn()
    manager.setPolicyHost({
      getSettings: () => ({ browserUse: { approvalMode: 'alwaysAsk', allowedDomains: [] } }),
      updateSettings
    })
    const { createTab } = await openSession(manager, owner, 'thread-1', '/workspace/test-root')

    const pending = createTab('https://example.com')

    await vi.waitFor(() => {
      expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:approval-request', expect.objectContaining({
        domain: 'example.com'
      }))
    })
    const payload = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls[0][1] as { requestId: string }
    expect(manager.handleApprovalResponse({ requestId: payload.requestId, action: 'allowOnce' })).toBe(true)

    expect((await pending).url).toBe('https://example.com/')
    expect(updateSettings).not.toHaveBeenCalled()
    const approvalRequests = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls.filter(
      ([channel]) => channel === 'viewer:browser:approval-request'
    )
    expect(approvalRequests).toHaveLength(1)
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, expect.objectContaining({
      url: 'https://example.com/'
    }))
  })

  it('still requires approval for explicit navigation after initial allow-once', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    manager.setPolicyHost({
      getSettings: () => ({ browserUse: { approvalMode: 'alwaysAsk', allowedDomains: [] } }),
      updateSettings: vi.fn()
    })
    const { call, createTab, navigate } = await openSession(manager, owner, 'thread-1', '/workspace/test-root')
    const approvalRequests = () => (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls.filter(
      ([channel]) => channel === 'viewer:browser:approval-request'
    )
    const approve = async (count: number, domain: string) => {
      await vi.waitFor(() => expect(approvalRequests()).toHaveLength(count))
      const payload = approvalRequests().find(([, request]) => request.domain === domain)?.[1] as { requestId: string } | undefined
      expect(payload).toBeDefined()
      expect(manager.handleApprovalResponse({ requestId: payload!.requestId, action: 'allowOnce' })).toBe(true)
    }

    const created = createTab('https://example.com')
    await approve(1, 'example.com')
    const tab = await created

    const navigation = navigate(tab, 'https://another.example')
    await approve(2, 'another.example')
    await navigation

    expect(await call('getTabs')).toMatchObject([{ url: 'https://another.example/' }])
  })

  it('applies viewport and visibility to controlled tabs and finalizes only tabs it created', async () => {
    const selectedTab = { tabId: 'existing-tab', currentUrl: 'http://127.0.0.1:3000/', title: 'Existing', loading: false }
    const host = createFakeHost()
    host.getAutomationTargetTab.mockReturnValue(selectedTab)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { call, command, createTab } = await openSession(manager, owner, 'thread-1')

    const [existing] = await call('getUserTabs') as Array<{ id: number }>
    const created = await createTab('localhost:3000')
    await command('browser_viewport_set', { width: 800, height: 600 })
    await command('browser_visibility_set', { visible: false })
    const visibility = await command('browser_visibility_get')
    const openTabs = await call('getUserTabs') as Array<{ id: number }>
    const finalized = await call('finalizeTabs', { keep: [] })

    const createdViewerId = host.createAutomationTab.mock.calls[0]![1].tabId
    expect(visibility).toEqual({ visible: false })
    expect(openTabs.map((tab) => tab.id).sort()).toEqual([existing!.id, created.id].sort())
    expect(finalized).toEqual({ ok: true, kept: [], closed: [created.id], released: [existing!.id] })
    expect(host.destroyTab).toHaveBeenCalledWith(owner, createdViewerId)
    expect(host.destroyTab).not.toHaveBeenCalledWith(owner, 'existing-tab')
    expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:close', {
      threadId: 'thread-1',
      tabId: createdViewerId
    })
    expect(host.setViewport.mock.calls).toEqual([
      [owner, { tabId: createdViewerId, viewport: { width: 800, height: 600 } }]
    ])
    expect(host.setVisible).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: 'existing-tab',
      visible: false
    }))
  })

  it('round-trips structured clipboard items through the virtual clipboard', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-rich-clipboard')
    const tab = await createTab('localhost:3000')

    await command('tab_clipboard_write', {
      tab_id: tab.id,
      items: [{ entries: [{ mime_type: 'text/plain', text: 'rich text' }], presentation_style: 'inline' }]
    })

    expect(await command('tab_clipboard_read', { tab_id: tab.id })).toEqual({
      items: [{ entries: [{ mime_type: 'text/plain', text: 'rich text' }], presentation_style: 'inline' }]
    })
    await expect(command('tab_clipboard_write', { tab_id: tab.id, items: [{ entries: [] }] }))
      .rejects.toThrow('clipboard_write items require at least one entry')
  })

  it('rejects unsupported unhandled commands with UnsupportedApi', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-unsupported')
    const tab = await createTab('localhost:3000')

    await expect(command('tab_content_export', { tab_id: tab.id })).rejects.toThrow('UnsupportedApi: tab_content_export')
    await expect(command('tab_unknown', { tab_id: tab.id })).rejects.toThrow('UnsupportedApi: executeUnhandledCommand(tab_unknown)')
  })

  it('reports a destroyed page as PageClosed', async () => {
    const wc = createFakeWebContents()
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const { command, createTab } = await openSession(manager, owner, 'thread-page-closed')
    const tab = await createTab('localhost:3000')
    ;(wc.isDestroyed as ReturnType<typeof vi.fn>).mockReturnValue(true)

    await expect(command('tab_screenshot', { tab_id: tab.id })).rejects.toThrow('PageClosed: Browser page is closed')
  })

  it('creates, lists, names, and finalizes tabs through the IAB backend', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-backend',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-backend', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-backend', turn_id: 'eval-1' }

    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>
    const tabId = Number(created.id)
    const tabs = await manager.handleBrowserUseBackendRequest('getTabs', session) as Array<Record<string, unknown>>
    const name = await manager.handleBrowserUseBackendRequest('nameSession', { ...session, name: 'backend docs' })
    await expect(manager.handleBrowserUseBackendRequest('finalizeTabs', {
      ...session,
      keep: [{ tabId }]
    })).rejects.toThrow('{ tabId, status: "deliverable"|"handoff" }')
    const finalized = await manager.handleBrowserUseBackendRequest('finalizeTabs', {
      ...session,
      keep: [{ tabId, status: 'handoff' }]
    }) as Record<string, unknown>

    expect(Number.isInteger(tabId)).toBe(true)
    expect(created.id).toBe(created.tabId)
    expect(String(created.id)).not.toMatch(/^browser-/)
    expect(created.id).toBe(tabId)
    expect(tabs).toHaveLength(1)
    expect(tabs[0].id).toBe(tabId)
    expect(name).toEqual({ ok: true, name: 'backend docs' })
    expect(finalized).toMatchObject({ ok: true, kept: [tabId], closed: [], released: [] })
  })

  it('notifies the renderer when backend close_tab closes a visible automation tab', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-backend-close',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-backend-close', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-backend-close', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>

    await manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'close_tab',
      tab_id: Number(created.id)
    })

    expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:close', {
      threadId: 'thread-backend-close',
      tabId: expect.stringMatching(/^browser-thread-backend-close-/)
    })
  })

  it('advertises M3 backend metadata and rejects hidden browser history', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-info',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-info', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-info', turn_id: 'eval-1' }

    const info = await manager.handleBrowserUseBackendRequest('getInfo', session) as Record<string, unknown>

    expect(info).toMatchObject({
      id: 'iab',
      protocolVersion: 2,
      supportsCommandCancel: true,
      supportsTypedFinalize: true,
      maxBrowserResultBytes: 1024 * 1024,
      metadata: { dotcraftSessionId: 'session-info' }
    })
    expect(Object.keys(info.metadata as Record<string, unknown>)).toEqual(['dotcraftSessionId'])
    await expect(manager.handleBrowserUseBackendRequest('getUserHistory', session))
      .rejects.toThrow('UnsupportedApi: browser.user.history is not supported by Desktop IAB')
  })

  it('handles browser visibility and viewport commands through the IAB backend fallback', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-browser-capabilities',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-browser-capabilities', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-browser-capabilities', turn_id: 'eval-1' }
    await manager.handleBrowserUseBackendRequest('createTab', session)

    await expect(manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'browser_visibility_set',
      visible: false
    })).resolves.toEqual({})
    await expect(manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'browser_visibility_get'
    })).resolves.toEqual({ visible: false })
    await expect(manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'browser_viewport_set',
      width: 900,
      height: 640
    })).resolves.toEqual({})
    await expect(manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'browser_viewport_reset'
    })).resolves.toEqual({})

    expect(host.setVisible).toHaveBeenCalledWith(owner, expect.objectContaining({ visible: false }))
    expect(host.setViewport.mock.calls.map(([, params]) => params.viewport)).toEqual([{ width: 900, height: 640 }, undefined])
  })

  it('returns normalized dev logs through the IAB backend fallback', async () => {
    const wc = createFakeWebContents()
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-dev-logs',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-dev-logs', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-dev-logs', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>
    ;(wc as unknown as EventEmitter).emit('console-message', {}, 2, 'warning from page')
    ;(wc as unknown as EventEmitter).emit('console-message', {}, 3, 'error from page')

    const result = await manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'tab_dev_logs',
      tab_id: created.id,
      filter: 'warning',
      levels: ['warn'],
      limit: 5
    }) as Record<string, unknown>

    expect(result).toMatchObject({
      logs: [{
        level: 'warn',
        message: 'warning from page',
        url: 'about:blank'
      }]
    })
  })

  it('returns browser.tabs.content results through temporary Desktop IAB tabs', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-tabs-content',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-tabs-content', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-tabs-content', turn_id: 'eval-1' }

    const text = await manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'tabs_content',
      urls: ['http://127.0.0.1:5173/text', 'http://127.0.0.1:5173/text-2'],
      content_type: 'text'
    }) as { results: Array<{ url: string; title: string | null; content: string | null }> }
    const html = await manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'tabs_content',
      urls: ['http://127.0.0.1:5173/html'],
      content_type: 'html'
    }) as { results: Array<{ url: string; title: string | null; content: string | null }> }
    const domSnapshot = await manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'tabs_content',
      urls: ['http://127.0.0.1:5173/dom'],
      content_type: 'domSnapshot'
    }) as { results: Array<{ url: string; title: string | null; content: string | null }> }

    expect(text.results[0]).toMatchObject({
      url: 'http://127.0.0.1:5173/text',
      title: 'Test Page',
      content: 'Save\nTest Link'
    })
    expect(text.results[1]).toMatchObject({
      url: 'http://127.0.0.1:5173/text-2',
      title: 'Test Page',
      content: 'Save\nTest Link'
    })
    expect(html.results[0]).toMatchObject({
      url: 'http://127.0.0.1:5173/html',
      title: 'Test Page'
    })
    expect(html.results[0].content).toContain('<button>Save</button>')
    expect(domSnapshot.results[0].content).toContain('Test Link')
    expect(host.destroyTab).toHaveBeenCalledTimes(4)
    expect(owner.webContents.send).not.toHaveBeenCalledWith('viewer:browser:open', expect.anything())
    expect(owner.webContents.send).not.toHaveBeenCalledWith('viewer:browser:close', expect.anything())
  })

  it('supports text clipboard through the IAB backend fallback', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('navigator.clipboard.readText')) return 'clipboard text'
      if (script.includes('navigator.clipboard.writeText')) return undefined
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-clipboard',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-clipboard', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-clipboard', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>

    await expect(manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'tab_clipboard_write_text',
      tab_id: created.id,
      text: 'clipboard text'
    })).resolves.toEqual({})
    await expect(manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'tab_clipboard_read_text',
      tab_id: created.id
    })).resolves.toEqual({ text: 'clipboard text' })
    await expect(manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'tab_clipboard_read',
      tab_id: created.id
    })).resolves.toMatchObject({
      items: [{
        entries: [{ mime_type: 'text/plain', text: 'clipboard text' }],
        presentation_style: 'unspecified'
      }]
    })

    const scripts = (wc.executeJavaScript as ReturnType<typeof vi.fn>).mock.calls
      .map(([script]) => String(script))
    expect(scripts.some((script) => script.includes('navigator.clipboard.writeText("clipboard text")'))).toBe(true)
    expect(scripts.some((script) => script.includes('navigator.clipboard.readText()'))).toBe(false)
  })

  it('executes Runtime.evaluate and Page.captureScreenshot through Electron debugger', async () => {
    const wc = createFakeWebContents()
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-cdp',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-cdp', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-cdp', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>
    const target = { tabId: created.id }

    const evaluated = await manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target,
      method: 'Runtime.evaluate',
      commandParams: { expression: '1 + 1' }
    })
    const screenshot = await manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target,
      method: 'Page.captureScreenshot',
      commandParams: { format: 'png' }
    })
    const scrolled = await manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target,
      method: 'DOM.scrollIntoViewIfNeeded',
      commandParams: { backendNodeId: 42 }
    })
    const mouse = await manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target,
      method: 'Input.dispatchMouseEvent',
      commandParams: { type: 'mouseReleased', x: 12, y: 34, button: 'left', buttons: 0, clickCount: 1 }
    })
    const key = await manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target,
      method: 'Input.dispatchKeyEvent',
      commandParams: { type: 'keyDown', key: 'Enter', code: 'Enter' }
    })

    expect(evaluated).toEqual({ result: { value: 'ok' } })
    expect(screenshot).toEqual({ data: 'AQID' })
    expect(scrolled).toEqual({})
    expect(mouse).toEqual({})
    expect(key).toEqual({})
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Runtime.evaluate', { expression: '1 + 1' })
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Page.captureScreenshot', { format: 'png' })
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('DOM.scrollIntoViewIfNeeded', { backendNodeId: 42 })
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Input.dispatchMouseEvent', {
      type: 'mouseReleased',
      x: 12,
      y: 34,
      button: 'left',
      buttons: 0,
      clickCount: 1
    })
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Input.dispatchKeyEvent', {
      type: 'keyDown',
      key: 'Enter',
      code: 'Enter'
    })
  })

  it('enables focus emulation before each Input command and rejects other Input methods', async () => {
    const wc = createFakeWebContents()
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-input',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-input', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-input', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>
    const execute = (method: string, commandParams: Record<string, unknown>) =>
      manager.handleBrowserUseBackendRequest('executeCdp', { ...session, target: { tabId: created.id }, method, commandParams })

    await execute('Input.dispatchMouseEvent', { type: 'mouseMoved', x: 1, y: 2 })
    await execute('Input.dispatchKeyEvent', { type: 'keyDown', key: 'a' })
    await execute('Input.insertText', { text: 'a' })
    await expect(execute('Input.synthesizeScrollGesture', { x: 1, y: 2, yDistance: -10 }))
      .rejects.toThrow('UnsupportedApi: Input.synthesizeScrollGesture')
    await expect(execute('Input.dispatchTouchEvent', { type: 'touchStart', touchPoints: [] }))
      .rejects.toThrow('UnsupportedApi: Input.dispatchTouchEvent')

    const sent = (wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mock.calls.map(([method, params]) => [method, params])
    expect(sent).toEqual([
      ['Emulation.setFocusEmulationEnabled', { enabled: true }],
      ['Input.dispatchMouseEvent', { type: 'mouseMoved', x: 1, y: 2 }],
      ['Emulation.setFocusEmulationEnabled', { enabled: true }],
      ['Input.dispatchKeyEvent', { type: 'keyDown', key: 'a' }],
      ['Emulation.setFocusEmulationEnabled', { enabled: true }],
      ['Input.insertText', { text: 'a' }]
    ])
  })

  it('marks the tab automation-active for any CDP command', async () => {
    const wc = createFakeWebContents()
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-cdp-active',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-cdp-active', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-cdp-active', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>
    host.setAutomationState.mockClear()

    await manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target: { tabId: created.id },
      method: 'Runtime.evaluate',
      commandParams: { expression: '1 + 1' }
    })

    expect(host.setAutomationState).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: host.createAutomationTab.mock.calls[0]![1].tabId,
      active: true
    }))
  })

  it('maps stale CDP DOM node errors to NodeStale', async () => {
    const wc = createFakeWebContents()
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(async (method: string) => {
      if (method === 'DOM.scrollIntoViewIfNeeded') {
        throw new Error('No node with given id found')
      }
      return {}
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-node-stale',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-node-stale', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-node-stale', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>

    await expect(manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target: { tabId: created.id },
      method: 'DOM.scrollIntoViewIfNeeded',
      commandParams: { backendNodeId: 42 }
    })).rejects.toThrow('NodeStale: Browser node is no longer available: 42')
  })

  it('forwards Electron debugger CDP events with tab and session metadata', async () => {
    const wc = createFakeWebContents()
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const backendServer = (manager as unknown as {
      backendServer: { sendNotification: (method: string, params: Record<string, unknown>) => void }
    }).backendServer
    const notifySpy = vi.spyOn(backendServer, 'sendNotification')
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-events',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-events', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-events', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>

    await manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target: { tabId: created.id },
      method: 'Page.enable',
      commandParams: {}
    })
    ;(wc.debugger as unknown as { emit(event: string, ...args: unknown[]): void })
      .emit('message', {}, 'Runtime.consoleAPICalled', { type: 'log' }, 'target-session-1')

    expect(notifySpy).toHaveBeenCalledWith('onCDPEvent', {
      source: { tabId: Number(created.id), sessionId: 'target-session-1' },
      method: 'Runtime.consoleAPICalled',
      params: { type: 'log' }
    })
  })

  it('dispatches CDP commands through attached target sessions', async () => {
    const wc = createFakeWebContents()
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(async (
      method: string,
      params?: Record<string, unknown>
    ) => {
      if (method === 'Target.attachToTarget') return { sessionId: 'session-for-target' }
      if (method === 'Runtime.evaluate') return { result: { value: 4 } }
      return { ok: true, params }
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-target',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-target', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-target', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>

    await manager.handleBrowserUseBackendRequest('attachTarget', {
      ...session,
      tabId: created.id,
      targetId: 'frame-target'
    })
    const result = await manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target: { tabId: created.id, targetId: 'frame-target' },
      method: 'Runtime.evaluate',
      commandParams: { expression: '2 + 2' }
    })

    expect(result).toEqual({ result: { value: 4 } })
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith(
      'Runtime.evaluate',
      { expression: '2 + 2' },
      'session-for-target'
    )
  })

  it('returns UnsupportedApi when Electron cannot attach a target session', async () => {
    const wc = createFakeWebContents()
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(async (method: string) => {
      if (method === 'Target.attachToTarget') return {}
      return {}
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-unsupported-target',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-unsupported-target', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-unsupported-target', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>

    await expect(manager.handleBrowserUseBackendRequest('attachTarget', {
      ...session,
      tabId: created.id,
      targetId: 'oopif-target'
    })).rejects.toThrow('UnsupportedApi: attachTarget(oopif-target)')

    await expect(manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target: { tabId: created.id, targetId: 'oopif-target' },
      method: 'Runtime.evaluate',
      commandParams: { expression: '2 + 2' }
    })).rejects.toThrow('UnsupportedApi: target session oopif-target')
  })

  it('returns stable timeout and result-size errors for backend CDP commands', async () => {
    const wc = createFakeWebContents()
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(async (method: string) => {
      if (method === 'Runtime.evaluate') return { result: { value: 'x'.repeat(1024 * 1024 + 1) } }
      return await new Promise(() => {})
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-limits',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-limits', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-limits', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>

    await expect(manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target: { tabId: created.id },
      method: 'Runtime.evaluate',
      commandParams: { expression: 'large' }
    })).rejects.toThrow('ResultTooLarge:')

    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(async () => await new Promise(() => {}))
    await expect(manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target: { tabId: created.id },
      method: 'Runtime.getProperties',
      commandParams: {},
      timeoutMs: 1
    })).rejects.toMatchObject({
      code: -32010,
      data: {
        operation: 'executeCdp',
        cdpMethod: 'Runtime.getProperties',
        tabId: String(created.id)
      }
    })

    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(async (method: string) => {
      if (method === 'Page.captureScreenshot') return { data: 'AQID' }
      return {}
    })
    await expect(manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target: { tabId: created.id },
      method: 'Page.captureScreenshot',
      commandParams: { format: 'png' }
    })).resolves.toEqual({ data: 'AQID' })
  })

  it('checks Desktop browser policy before backend Page.navigate', async () => {
    const wc = createFakeWebContents()
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    manager.setPolicyHost({
      getSettings: () => ({ browserUse: { blockedDomains: ['example.com'] } }),
      updateSettings: vi.fn()
    })
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-policy',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-policy', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-policy', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockClear()

    await expect(manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target: { tabId: created.id },
      method: 'Page.navigate',
      commandParams: { url: 'https://example.com/' }
    })).rejects.toThrow('Blocked browser domain: example.com')

    expect(wc.debugger.sendCommand).not.toHaveBeenCalledWith('Page.navigate', expect.anything())
  })

  it('reports backend Page.navigate errorText as NavigationFailed', async () => {
    const wc = createFakeWebContents()
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(async (method: string) => {
      if (method === 'Page.navigate') return { frameId: 'main', errorText: 'net::ERR_NAME_NOT_RESOLVED' }
      return {}
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const backendServer = (manager as unknown as {
      backendServer: { sendNotification: (method: string, params: Record<string, unknown>) => void }
    }).backendServer
    const notifySpy = vi.spyOn(backendServer, 'sendNotification')
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-nav-error-text',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-nav-error-text', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-nav-error-text', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>

    await expect(manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target: { tabId: created.id },
      method: 'Page.navigate',
      commandParams: { url: 'http://127.0.0.1:5173/missing' }
    })).rejects.toMatchObject({
      code: -32012,
      message: 'NavigationFailed: net::ERR_NAME_NOT_RESOLVED',
      data: {
        errorDescription: 'net::ERR_NAME_NOT_RESOLVED',
        validatedURL: 'http://127.0.0.1:5173/missing',
        finalURL: 'about:blank',
        isMainFrame: true
      }
    })

    expect(notifySpy).toHaveBeenCalledWith('onCDPEvent', {
      source: { tabId: Number(created.id) },
      method: 'Page.navigationBlocked',
      params: expect.objectContaining({
        errorDescription: 'net::ERR_NAME_NOT_RESOLVED',
        validatedURL: 'http://127.0.0.1:5173/missing',
        finalURL: 'about:blank',
        isMainFrame: true
      })
    })
  })

  it('does not treat Chromium error pages as successful backend navigation', async () => {
    const wc = createFakeWebContents()
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(async (method: string) => {
      if (method === 'Page.navigate') {
        wc.setUrl('chrome-error://chromewebdata/')
        return { frameId: 'main' }
      }
      return {}
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-chromium-error-page',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-chromium-error-page', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-chromium-error-page', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>

    await expect(manager.handleBrowserUseBackendRequest('executeCdp', {
      ...session,
      target: { tabId: created.id },
      method: 'Page.navigate',
      commandParams: { url: 'http://127.0.0.1:5173/missing' }
    })).rejects.toThrow('NavigationFailed: Chromium error page after navigation.')
  })

  it('moves the viewer virtual cursor through the IAB backend', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-cursor',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-cursor', turnId: 'eval-1' }
    })
    const session = { session_id: 'session-cursor', turn_id: 'eval-1' }
    const created = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>

    await manager.handleBrowserUseBackendRequest('moveMouse', {
      ...session,
      tabId: created.id,
      x: 42,
      y: 84
    })

    expect(host.moveMouse).toHaveBeenCalledWith(owner, {
      tabId: expect.stringMatching(/^browser-thread-cursor-/),
      x: 42,
      y: 84,
      waitForArrival: true
    })
  })

  it('activates the viewer cursor on move and deactivates it when a turn or evaluation ends', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    activeManagers.add(manager)
    const owner = createFakeOwner()
    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-cleanup',
      evaluationId: 'eval-1',
      browserSession: { sessionId: 'session-cleanup', turnId: 'turn-1' }
    })
    const session = { session_id: 'session-cleanup', turn_id: 'turn-1' }
    const handoff = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>
    const deliverable = await manager.handleBrowserUseBackendRequest('createTab', session) as Record<string, unknown>
    const viewerId = (index: number) => host.createAutomationTab.mock.calls[index]![1].tabId as string
    await manager.handleBrowserUseBackendRequest('moveMouse', { ...session, tabId: handoff.id, x: 1, y: 2 })
    expect(host.setAutomationState).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: viewerId(0),
      active: true,
      action: 'move'
    }))
    await manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session, type: 'tab_mark', tabId: handoff.id, status: 'handoff'
    })
    await manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session, type: 'tab_mark', tabId: deliverable.id, status: 'deliverable'
    })

    host.setAutomationState.mockClear()
    manager.handleTurnNotification('turn/completed', { threadId: 'thread-cleanup', turn: { id: 'turn-1' } })
    const finished = host.setAutomationState.mock.calls.map(([, params]) => params)
    expect(finished.find(params => params.tabId === viewerId(0))).toMatchObject({ active: false })
    expect(finished.find(params => params.tabId === viewerId(0))!.release).toBeUndefined()
    expect(finished.find(params => params.tabId === viewerId(1))).toMatchObject({ active: false, release: true })

    host.setAutomationState.mockClear()
    manager.abortEvaluation('thread-cleanup')
    expect(host.setAutomationState).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: viewerId(0),
      active: false
    }))
  })
})

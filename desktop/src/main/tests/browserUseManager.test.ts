import { afterEach, describe, expect, it, vi } from 'vitest'
import { EventEmitter } from 'events'
import { readFile } from 'fs/promises'

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
    emit: emitter.emit.bind(emitter),
    isDestroyed: vi.fn(() => false),
    getURL: vi.fn(() => url),
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
      if (script.includes('document.elementFromPoint')) {
        return [{
          nodeId: null,
          tagName: 'button',
          role: 'button',
          visibleText: 'Save',
          ariaName: 'Save',
          testId: null,
          selector: { primary: 'button', candidates: ['button'] },
          boundingBox: { x: 10, y: 20, width: 100, height: 40 },
          preview: '<button> Save'
        }]
      }
      if (script.includes('__dotcraftPlaywrightInjected &&')) return false
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
      if (script.includes('__dotcraftBrowserUseResolveSelector')) {
        return [{
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
          const expression = String(params?.expression ?? '')
          if (
            expression.includes('__dotcraftBrowserUsePageAssets') ||
            expression.includes('__dotcraftBrowserUseResolveSelector') ||
            expression.includes('__dotcraftBrowserUseSnapshot') ||
            expression.includes('__dotcraftPlaywrightInjected &&') ||
            expression.includes('module.exports.InjectedScript') ||
            isReadinessProbe(expression)
          ) {
            return { result: { value: await api.executeJavaScript(expression) } }
          }
          if (expression.includes('document.title')) {
            return { result: { value: 'Test Page' } }
          }
          if (
            expression.includes('document.documentElement ? document.documentElement.outerHTML') ||
            expression.includes('document.body ? document.body.innerText')
          ) {
            const value = await api.executeJavaScript(expression)
            return { result: { value } }
          }
          if (expression.includes('location.href')) {
            return {
              result: {
                value: {
                  href: url,
                  readyState: 'complete'
                }
              }
            }
          }
          if (expression.includes('incrementalAriaSnapshot')) {
            return { result: { value: '- button "Save"' } }
          }
          if (expression.includes('fn(arg)') && expression.includes('=> value + 1') && expression.includes(', 41')) {
            return { result: { value: 42 } }
          }
          if (expression.includes('const element = document.elementFromPoint')) {
            return { result: { value: await api.executeJavaScript(expression) } }
          }
          if (expression.includes('querySelectorAll') || expression.includes('internal:') || expression.includes('InjectedScript')) {
            return { result: { value: 1 } }
          }
          const value = await api.executeJavaScript(expression)
          return { result: { value } }
        }
        if (method === 'Page.getFrameTree') {
          return { frameTree: { frame: { id: 'main-frame', url } } }
        }
        if (method === 'Page.createIsolatedWorld') {
          return { executionContextId: 7 }
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
  return {
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
    setBounds: vi.fn(),
    setVisible: vi.fn(),
    moveMouse: vi.fn(),
    clickMouse: vi.fn(),
    doubleClickMouse: vi.fn(),
    dragMouse: vi.fn(),
    scrollMouse: vi.fn(),
    typeText: vi.fn(),
    keypress: vi.fn()
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

async function runBrowserUse(
  manager: BrowserUseManager,
  owner: Electron.BrowserWindow,
  params: { threadId: string; workspacePath?: string; code: string }
) {
  activeManagers.add(manager)
  const runtime = await manager.prepareNodeRepl(owner as BrowserWindow, params)
  const AsyncFunction = Object.getPrototypeOf(async function () {}).constructor
  try {
    const value = await new AsyncFunction('agent', 'display', params.code)(runtime.agent, runtime.display)
    const collected = runtime.collect()
    return {
      resultText: value == null ? '' : typeof value === 'string' ? value : JSON.stringify(value, null, 2),
      images: collected.images,
      logs: collected.logs
    }
  } catch (error) {
    const collected = runtime.collect()
    return {
      error: error instanceof Error ? error.message : String(error),
      images: collected.images,
      logs: collected.logs
    }
  }
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

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: `
        await agent.browser.nameSession("mario-test");
        const tab = await agent.browser.tabs.new("localhost:3000");
        return await tab.url();
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('http://localhost:3000/')
    expect(host.createAutomationTab).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: expect.stringMatching(/^browser-thread-1-/),
      workspacePath: '/workspace/test-root',
      allowFileScheme: true,
      width: 1280,
      height: 720
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

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: `
        await agent.browser.capabilities.get("visibility").set(true);
        await agent.browser.tabs.new("localhost:3000");
      `
    })

    expect(result.error).toBeUndefined()
    expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:open', expect.objectContaining({
      focusMode: 'first-open'
    }))
  })

  it('creates a stable blank selected tab before taking the first DOM snapshot', async () => {
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

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-blank',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.tabs.selected();
        return await tab.domSnapshot();
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText!)).toMatchObject({
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

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-empty-ready-snapshot',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000/empty");
        return await tab.domSnapshot();
      `
    })

    expect(result.error).toBeUndefined()
    const snapshotText = result.resultText!
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

    const pending = runBrowserUse(manager, owner, {
      threadId: 'thread-timeout',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.tabs.selected();
        return await tab.domSnapshot();
      `
    })
    const result = await pending

    expect(result.error).toContain("Browser operation 'domSnapshot.ready' timed out")
    expect(result.error).toContain('browser-thread-timeout-')
    expect(result.error).toContain('about:blank')
    releaseScript?.()
    await scriptPromise
  }, 15_000)

  it('opens 127.0.0.1 dev server URLs through the viewer host', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.tabs.new("127.0.0.1:5173");
        return await tab.url();
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('http://127.0.0.1:5173/')
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

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-vitepress',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/");
        await tab.waitForLoadState("load");
        return await tab.domSnapshot();
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText!)).toMatchObject({
      title: 'DotCraft',
      bodyText: expect.stringContaining('Search')
    })
  })

  it('supports networkidle load state without hanging', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-networkidle',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/");
        await tab.waitForLoadState("networkidle", 1000);
        return await tab.url();
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('http://127.0.0.1:5173/')
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

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-domcontentloaded-ready',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/background");
        await tab.playwright.waitForLoadState("domcontentloaded", 1000);
        return await tab.url();
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('http://127.0.0.1:5173/background')
    expect((wc.executeJavaScript as ReturnType<typeof vi.fn>).mock.calls.some(([script]) => String(script).includes('requestAnimationFrame'))).toBe(false)
  })

  it('waitForURL observes SPA in-page navigation', async () => {
    const wc = createFakeWebContents()
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    ;(globalThis as Record<string, unknown>).__simulateSpaNavigation = () => {
      wc.setUrl('http://127.0.0.1:5173/desktop_guide')
      ;(wc as unknown as EventEmitter).emit('did-navigate-in-page')
    }
    const pending = runBrowserUse(manager, owner, {
      threadId: 'thread-spa-url',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/");
        setTimeout(() => {
          globalThis.__simulateSpaNavigation?.();
        }, 20);
        await tab.playwright.waitForURL(/desktop_guide/, { timeoutMs: 1000 });
        return await tab.url();
      `
    })

    const result = await pending
    delete (globalThis as Record<string, unknown>).__simulateSpaNavigation

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('http://127.0.0.1:5173/desktop_guide')
  })

  it('waitForLoadState rejects main-frame navigation failures', async () => {
    const wc = createFakeWebContents()
    let loading = false
    ;(wc.isLoading as ReturnType<typeof vi.fn>).mockImplementation(() => loading)
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    ;(globalThis as Record<string, unknown>).__setBrowserLoading = (value: boolean) => {
      loading = value
    }
    const pending = runBrowserUse(manager, owner, {
      threadId: 'thread-failed-load',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/missing");
        globalThis.__setBrowserLoading?.(true);
        try {
          await tab.playwright.waitForLoadState({ state: "load", timeoutMs: 1000 });
          return "resolved";
        } catch (error) {
          return error instanceof Error ? error.message : String(error);
        }
      `
    })
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

    const result = await pending
    delete (globalThis as Record<string, unknown>).__setBrowserLoading

    expect(result.error).toBeUndefined()
    expect(result.resultText).toContain('NavigationFailed: ERR_NAME_NOT_RESOLVED')
  })

  it('returns a readable timeout when screenshot capture hangs', async () => {
    const wc = createFakeWebContents()
    let releaseCapture: (() => void) | undefined
    ;(wc.capturePage as ReturnType<typeof vi.fn>).mockImplementation(() => new Promise((resolve) => {
      releaseCapture = () => resolve({ toPNG: () => Buffer.from([9, 9, 9]) })
    }))
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host, { operationMs: 25 })
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-shot-timeout',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/");
        return await tab.screenshot();
      `
    })

    expect(result.error).toContain("Browser operation 'screenshot' timed out")
    expect(result.error).toContain('http://127.0.0.1:5173/')
    releaseCapture?.()
  })

  it('captures full-page screenshots with the CDP page dimensions', async () => {
    const wc = createFakeWebContents()
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      if (method === 'Runtime.evaluate') {
        const value = await wc.executeJavaScript(String(params?.expression ?? ''))
        return { result: { value } }
      }
      if (method === 'Page.getLayoutMetrics') {
        return { contentSize: { x: 0, y: 0, width: 1280, height: 2400 } }
      }
      if (method === 'Page.captureScreenshot') {
        return { data: 'CQgH' }
      }
      return {}
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-full-page-shot',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/");
        return await tab.screenshot({ fullPage: true });
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toEqual({
      mediaType: 'image/png',
      dataBase64: 'CQgH'
    })
    expect(wc.capturePage).not.toHaveBeenCalled()
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Page.captureScreenshot', expect.objectContaining({
      captureBeyondViewport: true,
      clip: expect.objectContaining({
        width: 1280,
        height: 2400,
        scale: 1
      })
    }))
  })

  it('includes browser operation diagnostics when page JavaScript times out', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(() => new Promise(() => {}))
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host, { operationMs: 25 })
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-diag-timeout',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.tabs.selected();
        return await tab.domSnapshot();
      `
    })

    expect(result.error).toContain("Browser operation 'domSnapshot.ready' timed out")
    expect(result.logs.join('\n')).toContain('Recent browser operations')
    expect(result.logs.join('\n')).toContain('domSnapshot.ready')
  })

  it('does not force focus for background tabs in the same thread', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: 'await agent.browser.tabs.new("localhost:3000");'
    })
    await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: 'await agent.browser.tabs.new("localhost:3001");'
    })

    expect(owner.webContents.send).toHaveBeenNthCalledWith(1, 'viewer:browser:open', expect.objectContaining({
      focusMode: 'none'
    }))
    expect(owner.webContents.send).toHaveBeenNthCalledWith(2, 'viewer:browser:open', expect.objectContaining({
      focusMode: 'none'
    }))
  })

  it('adopts the current thread browser tab for default Node REPL navigation', async () => {
    const host = createFakeHost()
    host.getAutomationTargetTab.mockReturnValue({
      tabId: 'user-browser-tab',
      currentUrl: 'about:blank',
      title: 'User tab',
      loading: false
    })
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: 'const tab = await agent.browser.goto("localhost:5173"); return await tab.url();'
    })

    expect(result.error).toBeUndefined()
    expect(host.createAutomationTab).not.toHaveBeenCalled()
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, {
      tabId: 'user-browser-tab',
      url: 'http://localhost:5173/'
    })
    expect(host.setAutomationState).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: 'user-browser-tab',
      active: true,
      action: 'navigate'
    }))
  })

  it('reuses an adopted selected tab across Node REPL calls', async () => {
    const host = createFakeHost()
    host.getAutomationTargetTab.mockReturnValue({
      tabId: 'user-browser-tab',
      currentUrl: 'about:blank',
      title: 'User tab',
      loading: false
    })
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: 'await agent.browser.tabs.selected();'
    })
    await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: 'const tab = await agent.browser.tabs.selected(); await tab.goto("localhost:5174");'
    })

    expect(host.createAutomationTab).not.toHaveBeenCalled()
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, {
      tabId: 'user-browser-tab',
      url: 'http://localhost:5174/'
    })
  })

  it('keeps an existing selected runtime tab over a later automation target', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: 'await agent.browser.tabs.new("localhost:3000");'
    })
    host.loadAutomationUrl.mockClear()
    host.getAutomationTargetTab.mockReturnValue({
      tabId: 'user-browser-tab',
      currentUrl: 'about:blank',
      title: 'User tab',
      loading: false
    })

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: 'const tab = await agent.browser.goto("localhost:5174"); return tab.id;'
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toMatch(/^browser-thread-1-/)
    expect(host.getAutomationTargetTab).not.toHaveBeenCalled()
    expect(host.loadAutomationUrl).toHaveBeenCalledWith(owner, {
      tabId: expect.stringMatching(/^browser-thread-1-/),
      url: 'http://localhost:5174/'
    })
    expect(host.loadAutomationUrl).not.toHaveBeenCalledWith(owner, {
      tabId: 'user-browser-tab',
      url: 'http://localhost:5174/'
    })
  })

  it('reset leaves adopted user browser tabs open but clears automation state', async () => {
    const host = createFakeHost()
    host.getAutomationTargetTab.mockReturnValue({
      tabId: 'user-browser-tab',
      currentUrl: 'about:blank',
      title: 'User tab',
      loading: false
    })
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: 'await agent.browser.goto("localhost:5173");'
    })
    expect(manager.reset('thread-1')).toEqual({ ok: true })

    expect(host.destroyTab).not.toHaveBeenCalled()
    expect(host.setAutomationState).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: 'user-browser-tab',
      active: false
    }))
  })

  it('reset destroys viewer browser tabs for the thread', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: 'await agent.browser.tabs.new("localhost:3000");'
    })

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

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: 'const tab = await agent.browser.tabs.new("https://example.com"); return await tab.url();'
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('https://example.com/')
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

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: 'await agent.browser.tabs.new("https://example.com");'
    })

    expect(result.error).toContain('Blocked browser domain: example.com')
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

    const pending = runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: 'const tab = await agent.browser.tabs.new("https://example.com"); return await tab.url();'
    })

    await vi.waitFor(() => {
      expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:approval-request', expect.objectContaining({
        domain: 'example.com'
      }))
    })
    const payload = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls[0][1] as { requestId: string }
    expect(manager.handleApprovalResponse({ requestId: payload.requestId, action: 'allowDomain' })).toBe(true)

    const result = await pending
    expect(result.error).toBeUndefined()
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

    const pending = runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: 'const tab = await agent.browser.tabs.new("https://example.com"); return await tab.url();'
    })

    await vi.waitFor(() => {
      expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:approval-request', expect.objectContaining({
        domain: 'example.com'
      }))
    })
    const payload = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls[0][1] as { requestId: string }
    expect(manager.handleApprovalResponse({ requestId: payload.requestId, action: 'allowOnce' })).toBe(true)

    const result = await pending
    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('https://example.com/')
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

    const pending = runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.tabs.new("https://example.com");
        await tab.navigate("https://another.example");
        return await tab.url();
      `
    })

    await vi.waitFor(() => {
      expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:approval-request', expect.objectContaining({
        domain: 'example.com'
      }))
    })
    const firstPayload = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls[0][1] as { requestId: string }
    expect(manager.handleApprovalResponse({ requestId: firstPayload.requestId, action: 'allowOnce' })).toBe(true)

    await vi.waitFor(() => {
      expect((owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls.filter(
        ([channel]) => channel === 'viewer:browser:approval-request'
      )).toHaveLength(2)
    })
    const secondPayload = (owner.webContents.send as ReturnType<typeof vi.fn>).mock.calls.find(
      ([channel, payload]) => channel === 'viewer:browser:approval-request' && payload.domain === 'another.example'
    )?.[1] as { requestId: string } | undefined
    expect(secondPayload).toBeDefined()
    expect(manager.handleApprovalResponse({ requestId: secondPayload!.requestId, action: 'allowOnce' })).toBe(true)

    const result = await pending
    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('https://another.example/')
  })

  it('routes CUA click through the viewer host input layer', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        await tab.cua.click({ x: 40, y: 50 });
      `
    })

    expect(result.error).toBeUndefined()
    expect(host.clickMouse).toHaveBeenCalledWith(owner, expect.objectContaining({
      x: 40,
      y: 50
    }))
    expect(host.setAutomationState).toHaveBeenCalledWith(owner, expect.objectContaining({
      active: true,
      action: 'click'
    }))
  })

  it('exposes agent.browsers, browser capabilities, user tabs, and finalize', async () => {
    const selectedTab = { tabId: 'existing-tab', currentUrl: 'http://127.0.0.1:3000/', title: 'Existing', loading: false }
    const host = createFakeHost()
    host.getAutomationTargetTab.mockReturnValue(selectedTab)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: `
        const browsers = await agent.browsers.list();
        const browser = await agent.browsers.get("iab");
        const selected = await browser.tabs.selected();
        const created = await browser.tabs.new("localhost:3000");
        const viewport = await browser.capabilities.get("viewport");
        await viewport.set({ width: 800, height: 600 });
        const visibility = await browser.capabilities.get("visibility");
        await visibility.set(false);
        const visible = await visibility.get();
        const openTabs = await browser.user.openTabs();
        const finalized = await browser.tabs.finalize({ keep: [] });
        return JSON.stringify({
          browserCount: browsers.length,
          selectedId: selected.id,
          createdId: created.id,
          visible,
          openTabCount: openTabs.length,
          finalized
        });
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload.browserCount).toBe(1)
    expect(payload.selectedId).toBe('existing-tab')
    expect(payload.createdId).toMatch(/^browser-thread-1-/)
    expect(payload.visible).toBe(false)
    expect(payload.openTabCount).toBe(2)
    expect(payload.finalized.closed).toEqual([payload.createdId])
    expect(host.destroyTab).toHaveBeenCalledWith(owner, payload.createdId)
    expect(host.destroyTab).not.toHaveBeenCalledWith(owner, 'existing-tab')
    expect(owner.webContents.send).toHaveBeenCalledWith('viewer:browser:close', {
      threadId: 'thread-1',
      tabId: payload.createdId
    })
    expect(host.setBounds).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: 'existing-tab',
      width: 800,
      height: 600
    }))
    expect(host.setVisible).toHaveBeenCalledWith(owner, expect.objectContaining({
      tabId: 'existing-tab',
      visible: false
    }))
  })

  it('routes browser-use compatible backend command aliases through the Desktop runtime', async () => {
    const wc = createFakeWebContents()
    const defaultExecuteJavaScript = (wc.executeJavaScript as ReturnType<typeof vi.fn>).getMockImplementation()
    const defaultSendCommand = (wc.debugger.sendCommand as ReturnType<typeof vi.fn>).getMockImplementation()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('__dotcraftBrowserUsePageAssets')) {
        return {
          pageUrl: 'http://localhost:3000/',
          assets: [{
            kind: 'stylesheet',
            name: 'site.css',
            sources: [{ kind: 'attribute', nodeId: 1, property: 'href' }],
            url: 'data:text/css;base64,Ym9keXtjb2xvcjpyZWR9'
          }],
          inlineSvgs: []
        }
      }
      if (script.includes('__dotcraftWebMcpAvailabilityProbe')) return true
      if (script.includes('navigator.modelContext') && script.includes('modelContext.executeTool(tool')) {
        return { ok: true, topic: 'backend' }
      }
      if (script.includes('navigator.modelContext') && script.includes('modelContext.getTools')) {
        return [{
          name: 'summarize',
          title: 'Summarize',
          description: 'Summarize the current page.',
          inputSchema: { type: 'object', properties: { topic: { type: 'string' } } },
          annotations: { readOnlyHint: true },
          origin: 'http://localhost:3000',
          pageUrl: 'http://localhost:3000/'
        }]
      }
      if (script.includes('operation, arg') && script.includes('getAttribute')) return '/test'
      if (script.includes('operation, arg') && script.includes('isEnabled')) return true
      if (script.includes('operation, arg') && script.includes('textContent')) return 'Test Link'
      return defaultExecuteJavaScript?.(script)
    })
    ;(wc.debugger.sendCommand as ReturnType<typeof vi.fn>).mockImplementation(async (method: string, params?: Record<string, unknown>) => {
      const expression = String(params?.expression ?? '')
      if (method === 'Runtime.evaluate' && expression.includes('operation, arg') && expression.includes('getAttribute')) {
        return { result: { value: '/test' } }
      }
      return defaultSendCommand?.(method, params)
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const base = { session_id: 'session-alias', turn_id: 'turn-alias' }

    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-backend-alias',
      browserSession: {
        sessionId: base.session_id,
        turnId: base.turn_id
      }
    })
    const created = await manager.handleBrowserUseBackendRequest('createTab', {
      ...base,
      url: 'localhost:3000'
    }) as Record<string, unknown>
    const tabId = Number(created.id)
    const exec = async (type: string, extra: Record<string, unknown> = {}) =>
      await manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
        ...base,
        browser_id: 'iab',
        tab_id: tabId,
        type,
        ...extra
      }) as Record<string, unknown>

    const openTabs = await exec('browser_user_open_tabs')
    const claimed = await exec('browser_user_claim_tab')
    const screenshot = await exec('tab_screenshot')
    const evaluated = await exec('playwright_evaluate', { script: 'document.body ? document.body.innerText : ""' })
    const evaluatedWithArg = await exec('playwright_evaluate', { script: '(value) => value + 1', arg: 41, timeout_ms: 1000 })
    await expect(exec('playwright_evaluate', { script: 'window.scrollTo(0, 10)' })).rejects.toThrow('ReadonlyEvaluateViolation')
    await expect(exec('playwright_evaluate', { script: 'document.body.appendChild(document.createElement("div"))' })).rejects.toThrow('ReadonlyEvaluateViolation')
    const domSnapshot = await exec('playwright_dom_snapshot')
    await exec('playwright_wait_for_timeout', { timeout_ms: 0 })
    await exec('playwright_wait_for_load_state', { state: 'load', timeout_ms: 1000 })
    const waitUrl = await exec('playwright_wait_for_url', { url: 'http://localhost:3000/', timeout_ms: 1000 })
    const locatorCount = await exec('playwright_locator_count', { selector: 'button' })
    const locatorTexts = await exec('playwright_locator_all_text_contents', { selector: 'button' })
    const locatorAttribute = await exec('playwright_locator_get_attribute', { selector: 'button', name: 'href' })
    const locatorReadAll = await exec('playwright_locator_read_all', { selector: 'button' })
    await exec('playwright_locator_click', { selector: 'button' })
    await exec('playwright_locator_dblclick', { selector: 'button' })
    await exec('playwright_locator_fill', { selector: 'button', value: 'Ada', replace: true })
    await exec('playwright_locator_press', { selector: 'button', value: 'Enter' })
    await exec('playwright_locator_wait_for', { selector: 'button', state: 'visible', timeout_ms: 1000 })
    await exec('playwright_locator_select_option', { selector: 'select', selections: [{ value: 'a' }] })
    await exec('playwright_locator_set_checked', { selector: 'input[type=checkbox]', checked: true })
    await exec('cua_move', { x: 12, y: 18 })
    await exec('cua_click', { x: 12, y: 18 })
    await exec('cua_double_click', { x: 12, y: 18 })
    await exec('cua_drag', { path: [{ x: 12, y: 18 }, { x: 30, y: 40 }] })
    await exec('cua_keypress', { keys: ['Enter'] })
    await expect(exec('cua_scroll', { x: 12, y: 18 })).rejects.toThrow('Scroll requires a non-zero distance')
    await exec('cua_scroll', { x: 12, y: 18, scroll_x: 0, scroll_y: 80 })
    await exec('cua_type', { text: 'hello' })
    const visibleDom = await exec('dom_cua_get_visible_dom') as unknown as Array<Record<string, unknown>>
    await exec('dom_cua_click', { node_id: visibleDom[0].node_id })
    await exec('dom_cua_double_click', { node_id: visibleDom[0].node_id })
    await exec('dom_cua_keypress', { keys: ['Enter'] })
    await exec('dom_cua_scroll', { y: 120 })
    await exec('dom_cua_scroll', { node_id: visibleDom[0].node_id, y: 120 })
    await exec('dom_cua_scroll', { node_id: visibleDom[0].node_id, scroll_x: 0, scroll_y: 40 })
    await exec('dom_cua_type', { text: 'typed' })
    await exec('tab_clipboard_write', {
      items: [{ entries: [{ mime_type: 'text/plain', text: 'rich text' }], presentation_style: 'inline' }]
    })
    const clipboardItems = await exec('tab_clipboard_read')
    const assets = await exec('tab_page_assets_list')
    const bundle = await exec('tab_page_assets_bundle', { inventoryId: assets.id, kinds: ['stylesheet'] })
    const tools = await exec('webmcp_list_tools')
    const toolResult = await exec('webmcp_invoke_tool', { tool_name: 'summarize', input: { topic: 'backend' } })

    expect((openTabs.tabs as Array<Record<string, unknown>>)[0].id).toBe(String(tabId))
    expect(claimed.id).toBe(String(tabId))
    expect(screenshot.data).toBe('AQID')
    expect(evaluated.value).toContain('Save')
    expect(evaluatedWithArg.value).toBe(42)
    expect(String(domSnapshot.dom_snapshot)).toContain('Test Link')
    expect(waitUrl.url).toBe('http://localhost:3000/')
    expect(locatorCount.count).toBe(1)
    expect(locatorTexts.values).toEqual(['Test Link'])
    expect(locatorAttribute.value).toBe('/test')
    expect((locatorReadAll.values as Array<Record<string, unknown>>)[0]).toMatchObject({
      inner_text: 'Test Link',
      text_content: 'Test Link'
    })
    expect(host.moveMouse).toHaveBeenCalledWith(owner, expect.objectContaining({ x: 12, y: 18 }))
    expect(host.clickMouse).toHaveBeenCalled()
    expect(host.doubleClickMouse).toHaveBeenCalled()
    expect(host.dragMouse).toHaveBeenCalled()
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Input.synthesizeScrollGesture', expect.objectContaining({
      gestureSourceType: 'mouse',
      preventFling: true,
      speed: 8000
    }))
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Input.synthesizeScrollGesture', expect.objectContaining({
      x: 12,
      y: 18,
      yDistance: -80
    }))
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Input.synthesizeScrollGesture', expect.objectContaining({
      x: 640,
      y: 360,
      yDistance: -120
    }))
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Input.synthesizeScrollGesture', expect.objectContaining({
      x: 60,
      y: 40,
      yDistance: -120
    }))
    expect(host.typeText).toHaveBeenCalled()
    expect(host.keypress).toHaveBeenCalled()
    expect((clipboardItems.items as Array<Record<string, unknown>>)[0]).toMatchObject({
      presentation_style: 'inline'
    })
    expect((bundle.summary as Record<string, unknown>).downloadedCount).toBe(1)
    expect((tools.tools as Array<Record<string, unknown>>)[0]).toMatchObject({
      name: 'summarize',
      input_schema: { type: 'object', properties: { topic: { type: 'string' } } }
    })
    expect(toolResult.result).toEqual({ ok: true, topic: 'backend' })

    for (const type of ['browser_user_history', 'playwright_wait_for_download', 'playwright_wait_for_file_chooser', 'tab_content_export_gsuite']) {
      await expect(exec(type)).rejects.toThrow('UnsupportedApi:')
    }
  })

  it('lists and bundles page assets from the current rendered page state', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (isReadinessProbe(script)) {
        return {
          url: 'http://127.0.0.1:5173/',
          title: 'Asset Page',
          readyState: 'complete',
          bodyTextLength: 10,
          interactiveCount: 1,
          appRootTextLength: 10
        }
      }
      if (script.includes('__dotcraftBrowserUsePageAssets')) {
        return {
          pageUrl: 'http://127.0.0.1:5173/',
          assets: [
            {
              kind: 'stylesheet',
              name: 'site.css',
              sources: [{ kind: 'attribute', nodeId: 1, property: 'href' }],
              url: 'data:text/css;base64,Ym9keXtjb2xvcjpyZWR9'
            },
            {
              kind: 'script',
              name: 'app.js',
              sources: [{ kind: 'resource', property: 'script' }],
              url: 'http://127.0.0.1:5173/app.js'
            }
          ],
          inlineSvgs: [{ id: 'inline-svg-1', markup: '<svg aria-label="Logo"></svg>', name: 'Logo' }]
        }
      }
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-assets',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        const pageAssets = await tab.capabilities.get("pageAssets");
        const inventory = await pageAssets.list();
        const bundle = await pageAssets.bundle({ inventoryId: inventory.id, kinds: ["stylesheet"] });
        return JSON.stringify({ inventory, bundle });
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload.inventory.summary).toMatchObject({
      inlineSvgCount: 1,
      totalCount: 2
    })
    expect(payload.inventory.summary.byKind.stylesheet).toBe(1)
    expect(payload.inventory.assets[0].id).toMatch(/^stylesheet-/)
    expect(payload.bundle.summary).toMatchObject({
      requestedCount: 1,
      downloadedCount: 1,
      failedCount: 0
    })
    expect(payload.bundle.assets[0]).toMatchObject({
      contentType: 'text/css',
      kind: 'stylesheet',
      name: expect.stringContaining('site.css')
    })
    const manifest = JSON.parse(await readFile(payload.bundle.manifestPath, 'utf8'))
    expect(manifest.summary.downloadedCount).toBe(1)
  })

  it('omits WebMCP from tab capabilities on pages without page tools', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-webmcp-unavailable',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        const capabilities = await tab.capabilities.list();
        let getError = "";
        try {
          await tab.capabilities.get("webmcp");
        } catch (error) {
          getError = error instanceof Error ? error.message : String(error);
        }
        return JSON.stringify({
          ids: capabilities.map((capability) => capability.id),
          getError
        });
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toEqual({
      ids: ['pageAssets'],
      getError: 'Capability is not available: webmcp'
    })
  })

  it('returns a stable unavailable error for direct backend WebMCP commands on ordinary pages', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()
    const session = { session_id: 'session-webmcp-unavailable', turn_id: 'turn-webmcp-unavailable' }

    await manager.prepareNodeRepl(owner, {
      threadId: 'thread-webmcp-unavailable-backend',
      browserSession: {
        sessionId: session.session_id,
        turnId: session.turn_id
      }
    })
    const created = await manager.handleBrowserUseBackendRequest('createTab', {
      ...session,
      url: 'localhost:3000'
    }) as Record<string, unknown>
    const execute = async (type: string, extra: Record<string, unknown> = {}) =>
      await manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
        ...session,
        browser_id: 'iab',
        tab_id: Number(created.id),
        type,
        ...extra
      })

    await expect(execute('webmcp_list_tools')).rejects.toThrow('Capability is not available: webmcp')
    await expect(execute('webmcp_invoke_tool', { tool_name: 'summarize', input: { topic: 'iab' } }))
      .rejects.toThrow('Capability is not available: webmcp')
  })

  it('lists and invokes page-defined WebMCP tools through the tab capability', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (isReadinessProbe(script)) {
        return {
          url: 'http://127.0.0.1:5173/',
          title: 'WebMCP Page',
          readyState: 'complete',
          bodyTextLength: 12,
          interactiveCount: 1,
          appRootTextLength: 12
        }
      }
      if (script.includes('__dotcraftWebMcpAvailabilityProbe')) return true
      if (script.includes('navigator.modelContext') && script.includes('modelContext.executeTool(tool')) {
        return { ok: true, echo: { topic: 'iab' } }
      }
      if (script.includes('navigator.modelContext') && script.includes('modelContext.getTools')) {
        return [{
          name: 'summarize',
          title: 'Summarize',
          description: 'Summarize the current page.',
          inputSchema: { type: 'object', properties: { topic: { type: 'string' } } },
          annotations: { readOnlyHint: true },
          origin: 'http://127.0.0.1:5173',
          pageUrl: 'http://127.0.0.1:5173/'
        }]
      }
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-webmcp',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        const capabilities = await tab.capabilities.list();
        const webmcp = await tab.capabilities.get("webmcp");
        const tools = await webmcp.listTools();
        const direct = await webmcp.invokeTool({ toolName: "summarize", input: { topic: "iab" }, timeoutMs: 1000 });
        const viaTool = await tools[0].invoke({ topic: "iab" }, { timeoutMs: 1000 });
        return JSON.stringify({
          capabilityIds: capabilities.map((capability) => capability.id),
          name: tools[0].name,
          inputType: tools[0].inputSchema.type,
          readOnlyHint: tools[0].annotations.readOnlyHint,
          direct,
          viaTool
        });
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toEqual({
      capabilityIds: ['pageAssets', 'webmcp'],
      name: 'summarize',
      inputType: 'object',
      readOnlyHint: true,
      direct: { ok: true, echo: { topic: 'iab' } },
      viaTool: { ok: true, echo: { topic: 'iab' } }
    })
  })

  it('refreshes WebMCP tab capability availability after navigation', async () => {
    const wc = createFakeWebContents()
    const defaultExecuteJavaScript = (wc.executeJavaScript as ReturnType<typeof vi.fn>).getMockImplementation()
    const hasWebMcp = () => !String(wc.getURL()).includes('/plain')
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (isReadinessProbe(script)) {
        return {
          url: wc.getURL(),
          title: 'WebMCP Availability Page',
          readyState: 'complete',
          bodyTextLength: 12,
          interactiveCount: 1,
          appRootTextLength: 12
        }
      }
      if (script.includes('__dotcraftWebMcpAvailabilityProbe')) return hasWebMcp()
      if (script.includes('navigator.modelContext') && script.includes('modelContext.getTools') && hasWebMcp()) {
        return [{
          name: 'summarize',
          title: 'Summarize',
          description: 'Summarize the current page.',
          inputSchema: { type: 'object' }
        }]
      }
      return defaultExecuteJavaScript?.(script)
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-webmcp-navigation',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        const before = await tab.capabilities.list();
        await tab.goto("http://localhost:3000/plain");
        const after = await tab.capabilities.list();
        let getAfterError = "";
        try {
          await tab.capabilities.get("webmcp");
        } catch (error) {
          getAfterError = error instanceof Error ? error.message : String(error);
        }
        return JSON.stringify({
          before: before.map((capability) => capability.id),
          after: after.map((capability) => capability.id),
          getAfterError
        });
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toEqual({
      before: ['pageAssets', 'webmcp'],
      after: ['pageAssets'],
      getAfterError: 'Capability is not available: webmcp'
    })
  })

  it('keeps agent.browser as a compatibility alias', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: `
        const browser = await agent.browsers.get("iab");
        return JSON.stringify({
          sameTabs: browser.tabs.describeApi().join(",") === agent.browser.tabs.describeApi().join(","),
          browserApi: agent.browser.describeApi().includes('tabs.finalize({ keep: [{ tab, status: "deliverable"|"handoff" }] })')
        });
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toEqual({ sameTabs: true, browserApi: true })
  })

  it('requires typed finalize keep entries', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const legacy = await runBrowserUse(manager, owner, {
      threadId: 'thread-typed-finalize',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        await agent.browser.tabs.finalize({ keep: [tab] });
      `
    })
    const typed = await runBrowserUse(manager, owner, {
      threadId: 'thread-typed-finalize',
      code: `
        const tab = await agent.browser.tabs.selected();
        return await agent.browser.tabs.finalize({ keep: [{ tab, status: "deliverable" }] });
      `
    })

    expect(legacy.error).toContain('{ tab, status: "deliverable"|"handoff" }')
    expect(typed.error).toBeUndefined()
    expect(JSON.parse(typed.resultText ?? '{}')).toMatchObject({
      ok: true,
      kept: [expect.stringMatching(/^browser-thread-typed-finalize-/)]
    })
  })

  it('resolves locator clicks strictly and sends coordinate input', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('__dotcraftBrowserUseResolveSelector')) {
        return [{
          index: 0,
          tagName: 'button',
          role: 'button',
          name: 'Save',
          text: 'Save',
          selector: 'button',
          visible: true,
          enabled: true,
          visibleText: 'Save',
          ariaName: 'Save',
          boundingBox: { x: 10, y: 20, width: 100, height: 40 }
        }]
      }
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        await tab.playwright.getByRole("button", { name: "Save" }).click();
      `
    })

    expect(result.error).toBeUndefined()
    expect(host.clickMouse).toHaveBeenCalledWith(owner, expect.objectContaining({
      x: 60,
      y: 40
    }))
  })

  it('supports locator all, nth, scoped builders, allTextContents, check, setChecked, uncheck, and selectOption', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('__dotcraftPlaywrightInjected &&')) return false
      if (script.includes('module.exports.InjectedScript')) return true
      if (isReadinessProbe(script)) {
        return {
          url: 'http://127.0.0.1:5173/',
          title: 'DotCraft',
          readyState: 'complete',
          bodyTextLength: 30,
          interactiveCount: 2,
          appRootTextLength: 30
        }
      }
      if (script.includes('__dotcraftBrowserUseResolveSelector')) {
        return [{
          index: 0,
          tagName: 'button',
          role: 'button',
          name: 'First',
          text: 'First',
          selector: 'button',
          visible: true,
          enabled: true,
          visibleText: 'First',
          ariaName: 'First',
          boundingBox: { x: 10, y: 20, width: 100, height: 40 }
        }, {
          index: 1,
          tagName: 'button',
          role: 'button',
          name: 'Second',
          text: 'Second',
          selector: 'button',
          visible: true,
          enabled: true,
          visibleText: 'Second',
          ariaName: 'Second',
          boundingBox: { x: 210, y: 20, width: 100, height: 40 }
        }]
      }
      return true
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        const locator = tab.playwright.locator("button");
        const texts = await locator.allTextContents();
        const allLocators = await locator.all();
        const firstText = await allLocators[0].innerText();
        const scopedApi = typeof locator.getByLabel("First").count;
        await locator.nth(1).click({ timeoutMs: 1000 });
        await locator.first().check();
        await locator.first().setChecked(false);
        await locator.first().uncheck();
        await locator.first().selectOption("value-a");
        return JSON.stringify({ texts, allCount: allLocators.length, firstText, scopedApi });
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toEqual({
      texts: ['First', 'Second'],
      allCount: 2,
      firstText: 'First',
      scopedApi: 'function'
    })
    expect(host.clickMouse).toHaveBeenCalledWith(owner, expect.objectContaining({
      x: 260,
      y: 40
    }))
  })

  it('supports same-origin frameLocator through the Desktop Playwright compatibility API', async () => {
    const resolveScripts: string[] = []
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('__dotcraftPlaywrightInjected &&')) return false
      if (script.includes('module.exports.InjectedScript')) return true
      if (isReadinessProbe(script)) {
        return {
          url: 'http://127.0.0.1:5173/',
          title: 'Frame Page',
          readyState: 'complete',
          bodyTextLength: 30,
          interactiveCount: 1,
          appRootTextLength: 30
        }
      }
      if (script.includes('__dotcraftBrowserUseResolveSelector')) {
        resolveScripts.push(script)
        return [{
          index: 0,
          tagName: 'button',
          role: 'button',
          name: 'Save',
          text: 'Save',
          selector: 'button.save',
          visible: true,
          enabled: true,
          visibleText: 'Save',
          ariaName: 'Save',
          boundingBox: { x: 20, y: 30, width: 100, height: 40 }
        }]
      }
      return true
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-frame-locator',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        const frame = tab.playwright.frameLocator('iframe[name="preview"]');
        const count = await frame.getByRole("button", { name: "Save" }).count();
        await frame.locator("button.save").click();
        return JSON.stringify({
          count,
          nestedApi: typeof frame.frameLocator("iframe").getByText("Nested").count,
          hasFrameLocator: frame.describeApi().includes("frameLocator(selector)")
        });
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText ?? '{}')).toEqual({
      count: 1,
      nestedApi: 'function',
      hasFrameLocator: true
    })
    expect(resolveScripts.some((script) => script.includes('enter-frame'))).toBe(true)
    expect(resolveScripts.some((script) => script.includes('internal:role'))).toBe(true)
    expect(host.clickMouse).toHaveBeenCalledWith(owner, expect.objectContaining({
      x: 70,
      y: 50
    }))
  })

  it('supports DOM-CUA snapshots and node actions', async () => {
    const wc = createFakeWebContents()
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        const nodes = await tab.dom_cua.get_visible_dom();
        await tab.dom_cua.click({ node_id: nodes[0].node_id });
        await tab.dom_cua.type({ node_id: nodes[0].node_id, text: "hello" });
        await tab.dom_cua.keypress({ key: "Enter" });
        await tab.dom_cua.scroll({ y: 120 });
        await tab.dom_cua.scroll({ node_id: nodes[0].node_id, y: 120 });
        return JSON.stringify(nodes[0]);
      `
    })

    expect(result.error).toBeUndefined()
    const node = JSON.parse(result.resultText ?? '{}')
    expect(node.node_id).toBe('e1')
    expect(node.role).toBe('link')
    expect(host.clickMouse).toHaveBeenCalled()
    expect(host.typeText).toHaveBeenCalledWith(owner, expect.objectContaining({
      text: 'hello'
    }))
    expect(host.keypress).toHaveBeenCalledWith(owner, expect.objectContaining({
      keys: ['Enter']
    }))
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Input.synthesizeScrollGesture', expect.objectContaining({
      x: 640,
      y: 360,
      yDistance: -120,
      gestureSourceType: 'mouse',
      preventFling: true,
      speed: 8000
    }))
    expect(wc.debugger.sendCommand).toHaveBeenCalledWith('Input.synthesizeScrollGesture', expect.objectContaining({
      x: 60,
      y: 40,
      yDistance: -120,
      gestureSourceType: 'mouse',
      preventFling: true,
      speed: 8000
    }))
  })

  it('invalidates DOM-CUA node ids after navigation and tab close', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-dom-cua-stale',
      code: `
        const messageOf = async (action) => {
          try {
            await action();
            return "resolved";
          } catch (error) {
            return error instanceof Error ? error.message : String(error);
          }
        };
        const first = await agent.browser.tabs.new("localhost:3000");
        const firstNodes = await first.dom_cua.get_visible_dom();
        await first.goto("localhost:3001");
        const afterNavigation = await messageOf(() => first.dom_cua.click({ node_id: firstNodes[0].node_id }));
        const second = await agent.browser.tabs.new("localhost:3000");
        const secondNodes = await second.dom_cua.get_visible_dom();
        await second.close();
        const afterClose = await messageOf(() => second.dom_cua.click({ node_id: secondNodes[0].node_id }));
        const afterCloseTitle = await messageOf(() => second.title());
        return JSON.stringify({ afterNavigation, afterClose, afterCloseTitle });
      `
    })

    expect(result.error).toBeUndefined()
    const payload = JSON.parse(result.resultText ?? '{}')
    expect(payload.afterNavigation).toContain('NodeStale: Browser node is no longer available')
    expect(payload.afterClose).toContain('PageClosed: Browser page is closed')
    expect(payload.afterCloseTitle).toContain('PageClosed: Browser page is closed')
    expect(host.clickMouse).not.toHaveBeenCalled()
  })

  it('exposes unsupported APIs with clear errors', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        const messages = [];
        for (const action of [
          () => tab.playwright.waitForEvent("download"),
          () => tab.playwright.waitForEvent("filechooser"),
          () => tab.cua.download_media(),
          () => tab.dom_cua.download_media()
        ]) {
          try { await action(); } catch (error) { messages.push(error.message); }
        }
        return JSON.stringify(messages);
      `
    })

    expect(result.error).toBeUndefined()
    const messages = JSON.parse(result.resultText ?? '[]') as string[]
    expect(messages).toHaveLength(4)
    expect(messages.every((message) => message.includes('does not support'))).toBe(true)
  })

  it('reports strict failures for locator state-changing helpers', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('__dotcraftPlaywrightInjected &&')) return false
      if (script.includes('module.exports.InjectedScript')) return true
      if (isReadinessProbe(script)) {
        return {
          url: 'http://127.0.0.1:5173/',
          title: 'DotCraft',
          readyState: 'complete',
          bodyTextLength: 30,
          interactiveCount: 2,
          appRootTextLength: 30
        }
      }
      if (script.includes('__dotcraftBrowserUseResolveSelector')) {
        return [{
          index: 0,
          tagName: 'input',
          role: 'checkbox',
          name: 'A',
          text: 'A',
          selector: 'input[type="checkbox"]',
          visible: true,
          enabled: true,
          visibleText: 'A',
          ariaName: 'A',
          boundingBox: { x: 10, y: 20, width: 20, height: 20 }
        }, {
          index: 1,
          tagName: 'input',
          role: 'checkbox',
          name: 'B',
          text: 'B',
          selector: 'input[type="checkbox"]',
          visible: true,
          enabled: true,
          visibleText: 'B',
          ariaName: 'B',
          boundingBox: { x: 40, y: 20, width: 20, height: 20 }
        }]
      }
      return true
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        await tab.playwright.locator('input[type="checkbox"]').check();
      `
    })

    expect(result.error).toContain('Strict mode violation')
  })

  it('aligns getByRole link matching with DOM snapshot output', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (isReadinessProbe(script)) {
        return {
          url: 'http://127.0.0.1:5173/',
          title: 'DotCraft',
          readyState: 'complete',
          bodyTextLength: 46,
          interactiveCount: 1,
          appRootTextLength: 46
        }
      }
      if (script.includes('__dotcraftBrowserUseSnapshot')) {
        return {
          title: 'DotCraft',
          url: 'http://127.0.0.1:5173/',
          bodyText: 'DotCraft',
          elements: [{
            tag: 'a',
            role: 'link',
            name: 'Desktop',
            text: 'Desktop',
            href: '/desktop_guide',
            selector: 'a[href="/desktop_guide"]',
            visible: true,
            enabled: true,
            boundingBox: { x: 10, y: 20, width: 100, height: 40 }
          }]
        }
      }
      if (script.includes('__dotcraftBrowserUseResolveSelector')) {
        return [{
          index: 0,
          tagName: 'a',
          role: 'link',
          name: 'Desktop',
          text: 'Desktop',
          href: '/desktop_guide',
          selector: 'a[href="/desktop_guide"]',
          visible: true,
          enabled: true,
          visibleText: 'Desktop',
          ariaName: 'Desktop',
          boundingBox: { x: 10, y: 20, width: 100, height: 40 }
        }]
      }
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-role-align',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/");
        const snapshot = JSON.parse(await tab.domSnapshot());
        const count = await tab.playwright.getByRole("link", { name: "Desktop", exact: true }).count();
        return { count, element: snapshot.elements[0] };
      `
    })

    expect(result.error).toBeUndefined()
    expect(JSON.parse(result.resultText!)).toMatchObject({
      count: 1,
      element: {
        ref: 'e1',
        role: 'link',
        name: 'Desktop',
        selector: 'a[href="/desktop_guide"]'
      }
    })
  })

  it('lets agents click current snapshot refs without guessing selectors', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (isReadinessProbe(script)) {
        return {
          url: 'http://127.0.0.1:5173/',
          title: 'DotCraft',
          readyState: 'complete',
          bodyTextLength: 46,
          interactiveCount: 1,
          appRootTextLength: 46
        }
      }
      if (script.includes('__dotcraftBrowserUseSnapshot')) {
        return {
          title: 'DotCraft',
          url: 'http://127.0.0.1:5173/',
          bodyText: 'DotCraft',
          elements: [{
            tagName: 'a',
            role: 'link',
            name: 'Desktop',
            text: 'Desktop',
            href: '/desktop_guide',
            selector: 'a[href="/desktop_guide"]',
            visible: true,
            enabled: true,
            visibleText: 'Desktop',
            ariaName: 'Desktop',
            boundingBox: { x: 10, y: 20, width: 100, height: 40 }
          }]
        }
      }
      if (script.includes('__dotcraftBrowserUseResolveSelector')) {
        return [{
          index: 0,
          tagName: 'a',
          role: 'link',
          name: 'Desktop',
          text: 'Desktop',
          href: '/desktop_guide',
          selector: 'a[href="/desktop_guide"]',
          visible: true,
          enabled: true,
          visibleText: 'Desktop',
          ariaName: 'Desktop',
          boundingBox: { x: 10, y: 20, width: 100, height: 40 }
        }]
      }
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-ref-click',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/");
        const snapshot = JSON.parse(await tab.domSnapshot());
        await tab.playwright.clickRef(snapshot.elements[0].ref);
        return snapshot.accessibilitySnapshot;
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toContain('link "Desktop" [ref=e1]')
    expect(host.clickMouse).toHaveBeenCalledWith(owner, expect.objectContaining({
      x: 60,
      y: 40
    }))
  })

  it('fills snapshot refs that do not have generated selectors', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (isReadinessProbe(script)) {
        return {
          url: 'http://127.0.0.1:5173/',
          title: 'DotCraft',
          readyState: 'complete',
          bodyTextLength: 46,
          interactiveCount: 1,
          appRootTextLength: 46
        }
      }
      if (script.includes('__dotcraftPlaywrightInjected &&')) return false
      if (script.includes('module.exports.InjectedScript')) return true
      if (script.includes('__dotcraftBrowserUseSnapshot')) {
        return {
          title: 'DotCraft',
          url: 'http://127.0.0.1:5173/',
          bodyText: 'Search',
          elements: [{
            tagName: 'input',
            role: 'textbox',
            name: 'Search',
            text: '',
            testId: 'search-input',
            selector: '',
            visible: true,
            enabled: true,
            visibleText: '',
            ariaName: 'Search',
            boundingBox: { x: 10, y: 20, width: 200, height: 32 }
          }]
        }
      }
      return true
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-ref-fill-empty-selector',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/");
        const snapshot = JSON.parse(await tab.domSnapshot());
        await tab.playwright.fillRef(snapshot.elements[0].ref, "query");
      `
    })

    expect(result.error).toBeUndefined()
    expect(host.clickMouse).toHaveBeenCalledWith(owner, expect.objectContaining({
      x: 110,
      y: 36
    }))
  })

  it('reports stale or unknown snapshot refs clearly', async () => {
    const host = createFakeHost()
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-ref-missing',
      workspacePath: '/workspace/test-root',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/");
        await tab.playwright.clickRef("e404");
      `
    })

    expect(result.error).toContain("Unknown browser snapshot ref 'e404'")
    expect(result.error).toContain('Take a fresh domSnapshot()')
    expect(host.clickMouse).not.toHaveBeenCalled()
  })

  it('reports strict locator violations instead of guessing', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('__dotcraftBrowserUseResolveSelector')) {
        return [
          { index: 0, tagName: 'button', role: 'button', name: 'Save', text: 'Save', selector: 'button', visible: true, enabled: true, visibleText: 'Save', ariaName: 'Save', boundingBox: { x: 0, y: 0, width: 10, height: 10 } },
          { index: 1, tagName: 'button', role: 'button', name: 'Save', text: 'Save', selector: 'button', visible: true, enabled: true, visibleText: 'Save', ariaName: 'Save', boundingBox: { x: 20, y: 0, width: 10, height: 10 } }
        ]
      }
      return 'ok'
    })
    const host = createFakeHost(wc)
    const manager = new BrowserUseManager(host)
    const owner = createFakeOwner()

    const result = await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      code: `
        const tab = await agent.browser.tabs.new("localhost:3000");
        await tab.playwright.getByText("Save").click();
      `
    })

    expect(result.error).toContain('Strict mode violation')
    expect(host.clickMouse).not.toHaveBeenCalled()
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
    const selectedCommand = await manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'selected_tab'
    }) as Record<string, unknown>
    const listCommand = await manager.handleBrowserUseBackendRequest('executeUnhandledCommand', {
      ...session,
      type: 'list_tabs'
    }) as { tabs: Array<Record<string, unknown>> }
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
    expect(selectedCommand.id).toBe(String(tabId))
    expect(listCommand.tabs[0].id).toBe(String(tabId))
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
    expect(host.setBounds).toHaveBeenCalledWith(owner, expect.objectContaining({
      width: 900,
      height: 640
    }))
    expect(host.setBounds).toHaveBeenCalledWith(owner, expect.objectContaining({
      width: 1280,
      height: 720
    }))
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
    })).rejects.toThrow('CommandTimeout:')

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
    })).rejects.toThrow('NavigationFailed: net::ERR_NAME_NOT_RESOLVED')

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
})

import { describe, expect, it, vi } from 'vitest'
import { EventEmitter } from 'events'
import { readFile } from 'fs/promises'

vi.mock('electron', () => ({
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

function createFakeWebContents() {
  const emitter = new EventEmitter()
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
      if (script.includes('__dotcraftPlaywrightInjected &&')) return false
      if (script.includes('module.exports.InjectedScript')) return true
      if (script.includes('requestAnimationFrame') && script.includes('readyState')) {
        return {
          url,
          title: 'Test Page',
          readyState: 'complete',
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
      isAttached: vi.fn(() => debuggerAttached),
      attach: vi.fn(() => {
        debuggerAttached = true
      }),
      detach: vi.fn(() => {
        debuggerAttached = false
      }),
      sendCommand: vi.fn(async (method: string, params?: Record<string, unknown>) => {
        if (method === 'Runtime.evaluate') {
          const value = await api.executeJavaScript(String(params?.expression ?? ''))
          return { result: { value } }
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
  const runtime = manager.prepareNodeRepl(owner as BrowserWindow, params)
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
        const visibility = await agent.browser.capabilities.get("visibility");
        await visibility.set(true);
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
      if (script.includes('requestAnimationFrame') && script.includes('readyState')) {
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      if (script.includes('requestAnimationFrame') && script.includes('readyState')) {
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
      code: `
        const tab = await agent.browser.goto("http://127.0.0.1:5173/");
        await tab.waitForLoadState("networkidle", 1000);
        return await tab.url();
      `
    })

    expect(result.error).toBeUndefined()
    expect(result.resultText).toBe('http://127.0.0.1:5173/')
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
      code: 'await agent.browser.tabs.selected();'
    })
    await runBrowserUse(manager, owner, {
      threadId: 'thread-1',
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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

  it('lists and bundles page assets from the current rendered page state', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('requestAnimationFrame') && script.includes('readyState')) {
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
          browserApi: agent.browser.describeApi().includes("tabs.finalize({ keep })")
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

    expect(legacy.error).toContain('keep status must be')
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
      if (script.includes('requestAnimationFrame') && script.includes('readyState')) {
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

  it('supports DOM-CUA snapshots and node actions', async () => {
    const host = createFakeHost()
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
        await tab.dom_cua.scroll({ node_id: nodes[0].node_id, deltaY: 120 });
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
    expect(host.scrollMouse).toHaveBeenCalledWith(owner, expect.objectContaining({
      scrollY: 120
    }))
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
          () => tab.clipboard.read(),
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
    expect(messages).toHaveLength(5)
    expect(messages.every((message) => message.includes('does not support'))).toBe(true)
  })

  it('reports strict failures for locator state-changing helpers', async () => {
    const wc = createFakeWebContents()
    ;(wc.executeJavaScript as ReturnType<typeof vi.fn>).mockImplementation(async (script: string) => {
      if (script.includes('__dotcraftPlaywrightInjected &&')) return false
      if (script.includes('module.exports.InjectedScript')) return true
      if (script.includes('requestAnimationFrame') && script.includes('readyState')) {
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
      if (script.includes('requestAnimationFrame') && script.includes('readyState')) {
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
      workspacePath: 'F:/workspace',
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
      if (script.includes('requestAnimationFrame') && script.includes('readyState')) {
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
      workspacePath: 'F:/workspace',
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
      if (script.includes('requestAnimationFrame') && script.includes('readyState')) {
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
      workspacePath: 'F:/workspace',
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
      workspacePath: 'F:/workspace',
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
})

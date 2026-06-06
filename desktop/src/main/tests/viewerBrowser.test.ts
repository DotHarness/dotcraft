import { beforeEach, describe, expect, it, vi } from 'vitest'

const electronMock = vi.hoisted(() => {
  let currentUrl = 'about:blank'
  const loadURL = vi.fn(async (nextUrl: string) => {
    currentUrl = nextUrl
  })
  const webContents = {
    on: vi.fn(),
    once: vi.fn(),
    isDestroyed: vi.fn(() => false),
    close: vi.fn(),
    getURL: vi.fn(() => currentUrl),
    getTitle: vi.fn(() => 'DotCraft Browser'),
    isLoading: vi.fn(() => false),
    focus: vi.fn(),
    loadURL,
    reload: vi.fn(),
    stop: vi.fn(),
    setWindowOpenHandler: vi.fn(),
    sendInputEvent: vi.fn(),
    insertText: vi.fn(),
    executeJavaScript: vi.fn(async () => undefined),
    navigationHistory: {
      canGoBack: vi.fn(() => false),
      canGoForward: vi.fn(() => false),
      goBack: vi.fn(),
      goForward: vi.fn()
    }
  }
  const setBounds = vi.fn()
  const WebContentsView = vi.fn(() => ({ webContents, setBounds }))
  const fromPartition = vi.fn(() => ({
    protocol: { handle: vi.fn() },
    on: vi.fn(),
    setPermissionCheckHandler: vi.fn(),
    setPermissionRequestHandler: vi.fn()
  }))
  return {
    loadURL,
    webContents,
    setBounds,
    WebContentsView,
    fromPartition,
    reset() {
      currentUrl = 'about:blank'
      loadURL.mockClear()
      webContents.on.mockClear()
      webContents.once.mockClear()
      webContents.close.mockClear()
      webContents.reload.mockClear()
      webContents.stop.mockClear()
      webContents.focus.mockClear()
      webContents.setWindowOpenHandler.mockClear()
      webContents.sendInputEvent.mockClear()
      webContents.insertText.mockClear()
      webContents.executeJavaScript.mockClear()
      webContents.navigationHistory.canGoBack.mockClear()
      webContents.navigationHistory.canGoForward.mockClear()
      webContents.navigationHistory.goBack.mockClear()
      webContents.navigationHistory.goForward.mockClear()
      setBounds.mockClear()
      WebContentsView.mockClear()
      fromPartition.mockClear()
    }
  }
})

vi.mock('electron', () => ({
  BrowserWindow: { fromWebContents: vi.fn(() => null) },
  WebContentsView: electronMock.WebContentsView,
  nativeImage: { createFromBuffer: vi.fn(() => ({ isEmpty: () => true })) },
  session: { fromPartition: electronMock.fromPartition },
  shell: { openExternal: vi.fn(), openPath: vi.fn() }
}))

import {
  classifyBrowserUrl,
  loadOrReport,
  normalizeBrowserUrl,
  partitionForWorkspace,
  ViewerBrowserManager
} from '../viewerBrowser'

beforeEach(() => {
  electronMock.reset()
})

describe('normalizeBrowserUrl', () => {
  it('normalizes absolute http/https urls', () => {
    expect(normalizeBrowserUrl('https://example.com/docs')).toBe('https://example.com/docs')
    expect(normalizeBrowserUrl('http://example.com')).toBe('http://example.com/')
  })

  it('promotes host-like input to https', () => {
    expect(normalizeBrowserUrl('example.com')).toBe('https://example.com/')
    expect(normalizeBrowserUrl('docs.example.com/path')).toBe('https://docs.example.com/path')
  })

  it('promotes local development hosts to http', () => {
    expect(normalizeBrowserUrl('localhost:3000')).toBe('http://localhost:3000/')
    expect(normalizeBrowserUrl('127.0.0.1:5173/app')).toBe('http://127.0.0.1:5173/app')
    expect(normalizeBrowserUrl('[::1]:8080')).toBe('http://[::1]:8080/')
  })

  it('returns null for empty or control-character input', () => {
    expect(normalizeBrowserUrl('')).toBeNull()
    expect(normalizeBrowserUrl('   ')).toBeNull()
    expect(normalizeBrowserUrl('\u0000https://example.com')).toBeNull()
  })
})

describe('classifyBrowserUrl', () => {
  it('allows http/https and blocks unsupported schemes', () => {
    expect(classifyBrowserUrl('https://example.com')).toBe('allow')
    expect(classifyBrowserUrl('http://example.com')).toBe('allow')
    expect(classifyBrowserUrl('dotcraft-viewer://workspace/F%3A/workspace/index.html')).toBe('allow')
    expect(classifyBrowserUrl('file:///tmp/a.txt')).toBe('blocked')
    expect(classifyBrowserUrl('chrome://settings')).toBe('blocked')
    expect(classifyBrowserUrl('javascript:alert(1)')).toBe('blocked')
  })

  it('marks mailto/tel as external handoff', () => {
    expect(classifyBrowserUrl('mailto:test@example.com')).toBe('external-handoff')
    expect(classifyBrowserUrl('tel:10086')).toBe('external-handoff')
  })
})

describe('partitionForWorkspace', () => {
  it('creates deterministic partition ids', () => {
    const p1 = partitionForWorkspace('F:/examples/workspace')
    const p2 = partitionForWorkspace('F:/examples/workspace')
    expect(p1).toBe(p2)
    expect(p1.startsWith('persist:dotcraft-viewer:')).toBe(true)
  })

  it('is path-casing-insensitive on Windows style paths', () => {
    const upper = partitionForWorkspace('F:/DOTCRAFT/Workspace')
    const lower = partitionForWorkspace('f:/dotcraft/workspace')
    expect(upper).toBe(lower)
  })
})

describe('ViewerBrowserManager partition configuration', () => {
  it('installs the viewer protocol handler on browser partition sessions once', () => {
    const handle = vi.fn()
    const fakeSession = {
      protocol: { handle },
      on: vi.fn(),
      setPermissionCheckHandler: vi.fn(),
      setPermissionRequestHandler: vi.fn()
    } as unknown as Electron.Session
    const manager = new ViewerBrowserManager()

    manager.configurePartitionSession('persist:dotcraft-viewer:test', fakeSession)
    manager.configurePartitionSession('persist:dotcraft-viewer:test', fakeSession)

    expect(handle).toHaveBeenCalledTimes(1)
    expect(handle).toHaveBeenCalledWith('dotcraft-viewer', expect.any(Function))
  })
})

describe('loadOrReport', () => {
  it('emits did-fail-load and did-stop-loading when load rejects', async () => {
    const events: Array<{
      type: string
      message?: string
      url?: string
      errorDescription?: string
      validatedURL?: string
      finalURL?: string
      isMainFrame?: boolean
    }> = []
    await expect(loadOrReport({
      tabId: 'tab-1',
      url: 'https://example.com/',
      load: () => Promise.reject(new Error('load failed')),
      emit: (payload) => {
        events.push({
          type: payload.type,
          message: 'message' in payload ? payload.message : undefined,
          url: 'url' in payload ? payload.url : undefined,
          errorDescription: payload.errorDescription,
          validatedURL: payload.validatedURL,
          finalURL: payload.finalURL,
          isMainFrame: payload.isMainFrame
        })
      }
    })).resolves.toBeUndefined()

    expect(events).toHaveLength(2)
    expect(events[0]).toEqual({
      type: 'did-fail-load',
      message: 'load failed',
      url: 'https://example.com/',
      errorDescription: 'load failed',
      validatedURL: 'https://example.com/',
      finalURL: 'https://example.com/',
      isMainFrame: true
    })
    expect(events[1]).toEqual({
      type: 'did-stop-loading',
      message: undefined,
      url: 'https://example.com/',
      errorDescription: undefined,
      validatedURL: undefined,
      finalURL: undefined,
      isMainFrame: undefined
    })
  })

  it('ignores Electron ERR_ABORTED navigation cancellations', async () => {
    const events: unknown[] = []
    await expect(loadOrReport({
      tabId: 'tab-1',
      url: 'http://127.0.0.1:5173/',
      load: () => Promise.reject(new Error("ERR_ABORTED (-3) loading 'http://127.0.0.1:5173/'")),
      emit: (payload) => {
        events.push(payload)
      }
    })).resolves.toBeUndefined()

    expect(events).toHaveLength(0)
  })
})

describe('ViewerBrowserManager tab creation', () => {
  function createFakeWindow() {
    return {
      id: 1,
      isDestroyed: () => false,
      getContentBounds: () => ({ x: 0, y: 0, width: 1600, height: 900 }),
      webContents: {
        isDestroyed: () => false,
        send: vi.fn()
      },
      contentView: {
        addChildView: vi.fn(),
        removeChildView: vi.fn()
      }
    } as unknown as Electron.BrowserWindow
  }

  it('keeps the start page load for regular blank browser tabs', () => {
    const manager = new ViewerBrowserManager()

    manager.createTab(createFakeWindow(), {
      tabId: 'tab-regular',
      workspacePath: '/workspace/test-root',
      initialUrl: 'about:blank'
    })

    expect(electronMock.loadURL).toHaveBeenCalledTimes(1)
    expect(electronMock.loadURL.mock.calls[0]?.[0]).toContain('data:text/html')
  })

  it('does not load the start page for automation tabs before target navigation', () => {
    const manager = new ViewerBrowserManager()

    manager.createAutomationTab(createFakeWindow(), {
      tabId: 'tab-automation',
      workspacePath: '/workspace/test-root',
      initialUrl: 'about:blank'
    })

    expect(electronMock.loadURL).not.toHaveBeenCalled()
    expect(electronMock.setBounds).toHaveBeenCalledWith({
      x: -10000,
      y: -10000,
      width: 1280,
      height: 720
    })
  })

  it('does not attach a visible browser view before validated bounds arrive', () => {
    const manager = new ViewerBrowserManager()
    const win = createFakeWindow() as Electron.BrowserWindow & {
      contentView: { addChildView: ReturnType<typeof vi.fn> }
    }

    manager.createTab(win, {
      tabId: 'tab-regular',
      workspacePath: '/workspace/test-root',
      initialUrl: 'https://example.com'
    })
    manager.setVisible(win, { tabId: 'tab-regular', visible: true })

    expect(win.contentView.addChildView).not.toHaveBeenCalled()

    manager.setBounds(win, { tabId: 'tab-regular', x: 960, y: 80, width: 560, height: 720 })

    expect(electronMock.setBounds).toHaveBeenCalledWith({ x: 960, y: 80, width: 560, height: 720 })
    expect(win.contentView.addChildView).toHaveBeenCalledTimes(1)
  })

  it('rejects suspicious top-left partial browser bounds', () => {
    const manager = new ViewerBrowserManager()
    const win = createFakeWindow() as Electron.BrowserWindow & {
      contentView: { addChildView: ReturnType<typeof vi.fn> }
    }

    manager.createTab(win, {
      tabId: 'tab-regular',
      workspacePath: '/workspace/test-root',
      initialUrl: 'https://example.com'
    })
    manager.setVisible(win, { tabId: 'tab-regular', visible: true })
    manager.setBounds(win, { tabId: 'tab-regular', x: 0, y: 0, width: 900, height: 700 })

    expect(win.contentView.addChildView).not.toHaveBeenCalled()
    expect(electronMock.setBounds).not.toHaveBeenCalledWith({ x: 0, y: 0, width: 900, height: 700 })
    expect(electronMock.setBounds).toHaveBeenCalledWith({
      x: -10000,
      y: -10000,
      width: 1280,
      height: 720
    })
  })
})

describe('ViewerBrowserManager automation input', () => {
  function createAutomationHarness() {
    const events: unknown[] = []
    const webContents = {
      isDestroyed: vi.fn(() => false),
      focus: vi.fn(),
      sendInputEvent: vi.fn((event: unknown) => events.push(event)),
      insertText: vi.fn(),
      executeJavaScript: vi.fn(async () => undefined)
    }
    const setBounds = vi.fn()
    const win = {
      id: 1,
      isDestroyed: () => false,
      webContents: {
        isDestroyed: () => false,
        send: vi.fn()
      },
      contentView: {
        addChildView: vi.fn(),
        removeChildView: vi.fn()
      }
    } as unknown as Electron.BrowserWindow & { webContents: { send: ReturnType<typeof vi.fn> } }
    const manager = new ViewerBrowserManager()
    ;(manager as unknown as {
      byWindowId: Map<number, {
        tabs: Map<string, unknown>
        activeTabId: string | null
      }>
    }).byWindowId.set(1, {
      activeTabId: null,
      tabs: new Map([['tab-1', {
        tabId: 'tab-1',
        workspacePath: '/workspace/test-root',
        view: { webContents, setBounds },
        desiredVisible: true,
        visible: true,
        boundsInitialized: true,
        currentUrl: 'http://localhost:3000/',
        title: 'Test',
        automationEnabled: true
      }]])
    })
    return { manager, win, webContents, setBounds, events }
  }

  it('initializes the virtual cursor at the automation tab center', () => {
    const manager = new ViewerBrowserManager()
    const win = {
      id: 1,
      isDestroyed: () => false,
      webContents: {
        isDestroyed: () => false,
        send: vi.fn()
      },
      contentView: {
        addChildView: vi.fn(),
        removeChildView: vi.fn()
      }
    } as unknown as Electron.BrowserWindow & { webContents: { send: ReturnType<typeof vi.fn> } }

    manager.createAutomationTab(win, {
      tabId: 'tab-center',
      workspacePath: '/workspace/test-root',
      width: 1280,
      height: 900
    })

    expect(win.webContents.send).toHaveBeenCalledWith(
      'viewer:browser:event',
      expect.objectContaining({
        tabId: 'tab-center',
        type: 'virtual-cursor',
        x: 640,
        y: 450
      })
    )
  })

  it('recenters the virtual cursor on real bounds until the agent moves it', async () => {
    const { manager, win, setBounds } = createAutomationHarness()

    manager.setBounds(win, { tabId: 'tab-1', x: 20, y: 30, width: 800, height: 600 })

    expect(setBounds).toHaveBeenCalledWith({ x: 20, y: 30, width: 800, height: 600 })
    expect(win.webContents.send).toHaveBeenCalledWith(
      'viewer:browser:event',
      expect.objectContaining({
        tabId: 'tab-1',
        type: 'virtual-cursor',
        x: 400,
        y: 300
      })
    )

    win.webContents.send.mockClear()
    await manager.moveMouse(win, { tabId: 'tab-1', x: 10, y: 20 })
    win.webContents.send.mockClear()

    manager.setBounds(win, { tabId: 'tab-1', x: 20, y: 30, width: 1000, height: 700 })

    expect(win.webContents.send).not.toHaveBeenCalledWith(
      'viewer:browser:event',
      expect.objectContaining({
        tabId: 'tab-1',
        type: 'virtual-cursor',
        x: 500,
        y: 350
      })
    )
  })

  it('clickMouse sends move, down, and up input events', async () => {
    const { manager, win, webContents, events } = createAutomationHarness()

    await manager.clickMouse(win, { tabId: 'tab-1', x: 10, y: 20 })

    expect(webContents.executeJavaScript).toHaveBeenCalled()
    const scripts = (webContents.executeJavaScript.mock.calls as unknown[][]).map((call) => String(call[0]))
    expect(scripts.join('\n')).toContain("width: '28px'")
    expect(scripts.join('\n')).toContain("width: '40px'")
    const mouseMoves = events.filter((event) => (event as { type?: string }).type === 'mouseMove')
    expect(mouseMoves.length).toBeGreaterThan(1)
    expect(mouseMoves.at(-1)).toMatchObject({ type: 'mouseMove', x: 10, y: 20 })
    expect(events.at(-2)).toMatchObject({ type: 'mouseDown', x: 10, y: 20, button: 'left' })
    expect(events.at(-1)).toMatchObject({ type: 'mouseUp', x: 10, y: 20, button: 'left' })
  })

  it('does not block native click input when the visual overlay hangs', async () => {
    const { manager, win, webContents, events } = createAutomationHarness()
    webContents.executeJavaScript.mockImplementation(() => new Promise(() => {}))

    await expect(manager.clickMouse(win, { tabId: 'tab-1', x: 10, y: 20 })).resolves.toBeUndefined()

    expect(webContents.focus).toHaveBeenCalled()
    const mouseMoves = events.filter((event) => (event as { type?: string }).type === 'mouseMove')
    expect(mouseMoves.length).toBeGreaterThan(1)
    expect(mouseMoves.at(-1)).toMatchObject({ type: 'mouseMove', x: 10, y: 20 })
    expect(events.at(-2)).toMatchObject({ type: 'mouseDown', x: 10, y: 20, button: 'left' })
    expect(events.at(-1)).toMatchObject({ type: 'mouseUp', x: 10, y: 20, button: 'left' })
  })

  it('scrollMouse sends wheel input through the tab webContents', async () => {
    const { manager, win, events } = createAutomationHarness()

    await manager.scrollMouse(win, { tabId: 'tab-1', x: 5, y: 6, scrollX: 0, scrollY: 120 })

    expect(events.at(-1)).toMatchObject({
      type: 'mouseWheel',
      x: 5,
      y: 6,
      deltaY: 120
    })
  })

  it('keypress sends keyDown and keyUp with modifiers', () => {
    const { manager, win, events } = createAutomationHarness()

    manager.keypress(win, { tabId: 'tab-1', keys: ['Control', 'A'] })

    expect(events).toMatchObject([
      { type: 'keyDown', keyCode: 'A', modifiers: ['control'] },
      { type: 'keyUp', keyCode: 'A', modifiers: ['control'] }
    ])
  })

  it('returns the active browser tab for the requested thread as automation target', () => {
    const { manager, win } = createAutomationHarness()
    const webContents = {
      isDestroyed: vi.fn(() => false),
      getURL: vi.fn(() => 'http://localhost:5173/'),
      getTitle: vi.fn(() => 'Local app'),
      isLoading: vi.fn(() => false),
      navigationHistory: {
        canGoBack: vi.fn(() => false),
        canGoForward: vi.fn(() => false)
      }
    }
    ;(manager as unknown as {
      byWindowId: Map<number, {
        tabs: Map<string, unknown>
        activeTabId: string | null
      }>
    }).byWindowId.set(1, {
      activeTabId: 'tab-current',
      tabs: new Map([
        ['tab-other', {
          tabId: 'tab-other',
          threadId: 'thread-other',
          workspacePath: '/workspace/test-root',
          view: { webContents },
          desiredVisible: true,
          visible: true,
          boundsInitialized: true,
          currentUrl: 'http://localhost:4000/',
          title: 'Other'
        }],
        ['tab-current', {
          tabId: 'tab-current',
          threadId: 'thread-a',
          workspacePath: '/workspace/test-root',
          view: { webContents },
          desiredVisible: true,
          visible: true,
          boundsInitialized: true,
          currentUrl: 'http://localhost:5173/',
          title: 'Local app'
        }]
      ])
    })

    expect(manager.getAutomationTargetTab(win, 'thread-a')).toMatchObject({
      tabId: 'tab-current',
      threadId: 'thread-a',
      currentUrl: 'http://localhost:5173/',
      title: 'Local app'
    })
    expect(manager.getAutomationTargetTab(win, 'thread-missing')).toBeNull()
  })
})

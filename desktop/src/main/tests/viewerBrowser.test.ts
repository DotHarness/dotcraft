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
    setBackgroundThrottling: vi.fn(),
    close: vi.fn(),
    getURL: vi.fn(() => currentUrl),
    getUserAgent: vi.fn(() => 'Mozilla/5.0 Chrome/148.0.7778.97 Safari/537.36'),
    getTitle: vi.fn(() => 'DotCraft Browser'),
    isLoading: vi.fn(() => false),
    isCurrentlyAudible: () => false,
    isBeingCaptured: () => false,
    loadURL,
    reload: vi.fn(),
    stop: vi.fn(),
    setWindowOpenHandler: vi.fn(),
    navigationHistory: {
      canGoBack: vi.fn(() => false),
      canGoForward: vi.fn(() => false),
      goBack: vi.fn(),
      goForward: vi.fn()
    }
  }
  const partitionSession = {
    protocol: { handle: vi.fn() },
    on: vi.fn(),
    getUserAgent: vi.fn(() => 'Mozilla/5.0 Chrome/148.0.7778.97 Electron/42.0.1 DotCraft/0.7.2 Safari/537.36'),
    setUserAgent: vi.fn(),
    webRequest: { onBeforeSendHeaders: vi.fn(), onBeforeRequest: vi.fn() },
    setPermissionCheckHandler: vi.fn(),
    setPermissionRequestHandler: vi.fn()
  }
  Object.assign(webContents, { session: partitionSession })
  const fromPartition = vi.fn(() => partitionSession)
  return {
    loadURL,
    webContents,
    fromPartition,
    partitionSession,
    reset() {
      currentUrl = 'about:blank'
      vi.clearAllMocks()
    }
  }
})

vi.mock('electron', () => ({
  app: {
    getName: vi.fn(() => 'DotCraft'),
    getPreferredSystemLanguages: vi.fn(() => ['zh-CN', 'en-US']),
    getLocale: vi.fn(() => 'zh-CN'),
    getPath: vi.fn(() => 'C:/tmp')
  },
  BrowserWindow: { fromWebContents: vi.fn(() => null) },
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

describe('browser URL policy', () => {
  it('normalizes typed input, preferring http for local development hosts', () => {
    expect(normalizeBrowserUrl('http://example.com')).toBe('http://example.com/')
    expect(normalizeBrowserUrl('docs.example.com/path')).toBe('https://docs.example.com/path')
    expect(normalizeBrowserUrl('127.0.0.1:5173/app')).toBe('http://127.0.0.1:5173/app')
    expect(normalizeBrowserUrl('[::1]:8080')).toBe('http://[::1]:8080/')
    expect(normalizeBrowserUrl('   ')).toBeNull()
    expect(normalizeBrowserUrl('\u0000https://example.com')).toBeNull()
  })

  it('allows web and viewer URLs, hands off mailto/tel, and blocks other schemes', () => {
    expect(classifyBrowserUrl('https://example.com')).toBe('allow')
    expect(classifyBrowserUrl('dotcraft-viewer://workspace/F%3A/workspace/index.html')).toBe('allow')
    expect(classifyBrowserUrl('file:///tmp/a.txt')).toBe('blocked')
    expect(classifyBrowserUrl('javascript:alert(1)')).toBe('blocked')
    expect(classifyBrowserUrl('mailto:test@example.com')).toBe('external-handoff')
    expect(classifyBrowserUrl('tel:10086')).toBe('external-handoff')
  })

  it('derives a stable, case-insensitive partition per workspace', () => {
    const partition = partitionForWorkspace('F:/EXAMPLE/Workspace')
    expect(partition).toBe(partitionForWorkspace('f:/example/workspace'))
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
  const win = { id: 1, isDestroyed: () => false, webContents: { isDestroyed: () => false, send: vi.fn() } } as unknown as Electron.BrowserWindow

  it('waits for a guest before loading a regular page', async () => {
    const manager = new ViewerBrowserManager()
    let ready!: (page: Electron.WebContents) => void
    vi.spyOn(manager.hosts, 'request').mockReturnValue(new Promise(resolve => { ready = resolve }))
    vi.spyOn(manager.hosts, 'list').mockReturnValue([{ tabId: 'tab' } as never])
    const creation = manager.createTab(win, { tabId: 'tab', workspacePath: '/workspace', initialUrl: 'https://example.com' })
    expect(electronMock.loadURL).not.toHaveBeenCalled()
    ready(electronMock.webContents as unknown as Electron.WebContents)
    await creation
    expect(electronMock.loadURL).toHaveBeenCalledWith('https://example.com/')
    expect(manager.getTabWebContents(win, 'tab')).toBe(electronMock.webContents)
  })

  it('does not attach an old pending creation after close and replacement', async () => {
    const manager = new ViewerBrowserManager()
    let ready!: (page: Electron.WebContents) => void
    vi.spyOn(manager.hosts, 'request')
      .mockReturnValueOnce(new Promise(resolve => { ready = resolve }))
      .mockResolvedValue(electronMock.webContents as unknown as Electron.WebContents)
    vi.spyOn(manager.hosts, 'list').mockReturnValue([{ tabId: 'tab' } as never])
    const old = manager.createTab(win, { tabId: 'tab', workspacePath: '/workspace', initialUrl: 'https://old.example/' })
    const rejected = expect(old).rejects.toThrow('closed')
    manager.destroyTab(win, 'tab')
    await manager.createTab(win, { tabId: 'tab', workspacePath: '/workspace', initialUrl: 'https://new.example/' })
    ready(electronMock.webContents as unknown as Electron.WebContents)
    await rejected
    expect(manager.snapshotState(win, 'tab')?.currentUrl).toBe('https://new.example/')
    expect(manager.getTabWebContents(win, 'tab')).toBe(electronMock.webContents)
    expect(electronMock.loadURL).toHaveBeenCalledOnce()
  })

  it('retains the same guest when its presentation changes', async () => {
    const manager = new ViewerBrowserManager()
    const request = vi.spyOn(manager.hosts, 'request').mockResolvedValue(electronMock.webContents as unknown as Electron.WebContents)
    vi.spyOn(manager.hosts, 'list').mockReturnValue([{ tabId: 'tab' } as never])
    const update = vi.spyOn(manager.hosts, 'update')
    await manager.createTab(win, { tabId: 'tab', workspacePath: '/workspace' })
    manager.setVisible(win, { tabId: 'tab', visible: true })
    manager.setBounds(win, { tabId: 'tab', x: 12, y: 40, width: 600, height: 400 })
    expect(update).toHaveBeenLastCalledWith(win, 'tab', { bounds: { x: 12, y: 40, width: 600, height: 400 }, visible: true })
    manager.setVisible(win, { tabId: 'tab', visible: false })
    await manager.createTab(win, { tabId: 'tab', workspacePath: '/workspace' })
    expect(request).toHaveBeenCalledOnce()
    expect(electronMock.webContents.close).not.toHaveBeenCalled()
    expect(electronMock.loadURL).toHaveBeenCalledOnce()
  })

  it('keeps ordinary new-tab links in the viewer and named blank login windows as native children', async () => {
    const manager = new ViewerBrowserManager()
    vi.spyOn(manager.hosts, 'request').mockResolvedValue(electronMock.webContents as unknown as Electron.WebContents)
    vi.spyOn(manager.hosts, 'list').mockReturnValue([{ tabId: 'tab' } as never])
    await manager.createTab(win, { tabId: 'tab', workspacePath: '/workspace' })
    win.webContents.send.mockClear()
    const handler = electronMock.webContents.setWindowOpenHandler.mock.calls[0][0]

    const response = handler({
      url: 'https://accounts.example.com/',
      frameName: '',
      features: '',
      disposition: 'foreground-tab',
      referrer: { url: '', policy: 'default' }
    })

    expect(response).toEqual({ action: 'deny' })
    expect(win.webContents.send).toHaveBeenCalledWith(
      'viewer:browser:event',
      expect.objectContaining({ type: 'request-new-tab', url: 'https://accounts.example.com/' })
    )

    const login = handler({
      url: 'about:blank',
      frameName: 'oauth-login',
      features: '',
      disposition: 'foreground-tab',
      referrer: { url: 'https://example.com/', policy: 'strict-origin-when-cross-origin' }
    })
    expect(login).toMatchObject({ action: 'allow', outlivesOpener: false })
    expect(login).not.toHaveProperty('createWindow')
  })

  it('allows script authentication popups with the opener session and hardened preferences', async () => {
    const manager = new ViewerBrowserManager()
    vi.spyOn(manager.hosts, 'request').mockResolvedValue(electronMock.webContents as unknown as Electron.WebContents)
    vi.spyOn(manager.hosts, 'list').mockReturnValue([{ tabId: 'tab' } as never])
    await manager.createTab(win, { tabId: 'tab', workspacePath: '/workspace' })
    const handler = electronMock.webContents.setWindowOpenHandler.mock.calls[0][0]

    const response = handler({
      url: 'https://accounts.example.com/oauth',
      frameName: 'oauth',
      features: 'width=500,height=650',
      disposition: 'new-window',
      referrer: { url: 'https://example.com/', policy: 'strict-origin-when-cross-origin' }
    })

    expect(response).toMatchObject({
      action: 'allow',
      outlivesOpener: false,
      overrideBrowserWindowOptions: {
        parent: win,
        autoHideMenuBar: true,
        webPreferences: {
          session: electronMock.partitionSession,
          nodeIntegration: false,
          nodeIntegrationInSubFrames: false,
          nodeIntegrationInWorker: false,
          contextIsolation: true,
          sandbox: true,
          webSecurity: true,
          allowRunningInsecureContent: false,
          webviewTag: false,
          plugins: false,
          devTools: true
        }
      }
    })

    const popupWebContents = {
      on: vi.fn(),
      setWindowOpenHandler: vi.fn(),
      setUserAgent: vi.fn(),
      session: electronMock.partitionSession
    }
    const popup = {
      webContents: popupWebContents,
      setMenuBarVisibility: vi.fn(),
      once: vi.fn(),
      isDestroyed: vi.fn(() => false),
      destroy: vi.fn()
    } as unknown as Electron.BrowserWindow
    const didCreateWindow = electronMock.webContents.on.mock.calls
      .find(([event]) => event === 'did-create-window')?.[1]
    didCreateWindow(popup)

    expect((popup as unknown as { setMenuBarVisibility: ReturnType<typeof vi.fn> }).setMenuBarVisibility)
      .toHaveBeenCalledWith(false)
    expect(popupWebContents.setWindowOpenHandler).toHaveBeenCalledOnce()
    expect(popupWebContents.setUserAgent).toHaveBeenCalledWith(
      'Mozilla/5.0 Chrome/148.0.7778.97 Safari/537.36'
    )

    manager.destroyTab(win, 'tab')
    expect((popup as unknown as { destroy: ReturnType<typeof vi.fn> }).destroy).toHaveBeenCalledOnce()
  })
})

describe('ViewerBrowserManager automation input', () => {
  function createAutomationHarness() {
    const manager = new ViewerBrowserManager()
    const win = { id: 1, isDestroyed: () => false, webContents: { isDestroyed: () => false, send: vi.fn() } } as unknown as Electron.BrowserWindow
    vi.spyOn(manager.hosts, 'request').mockResolvedValue(electronMock.webContents as unknown as Electron.WebContents)
    vi.spyOn(manager.hosts, 'list').mockImplementation(() => ['tab-current', 'tab-other', 'tab-welcome'].map(tabId => ({ tabId }) as never))
    return { manager, win }
  }

  it('isolates automation discovery when different projects contain the same thread id', async () => {
    const { manager, win } = createAutomationHarness()
    await manager.createTab(win, { tabId: 'tab-current', projectKey: 'project:a', threadId: 'same', workspacePath: '/workspace/a' })
    await manager.createTab(win, { tabId: 'tab-other', projectKey: 'project:b', threadId: 'same', workspacePath: '/workspace/b' })
    manager.setActiveTab(win, 'tab-other')
    expect(manager.listAutomationTargetTabs(win, 'same', 'project:a').map(tab => tab.tabId)).toEqual(['tab-current'])
    expect(manager.listAutomationTargetTabs(win, 'same', 'project:missing')).toEqual([])
  })

  it('rebinds Welcome draft pages so automation finds them on the created thread', async () => {
    const { manager, win } = createAutomationHarness()
    await manager.createTab(win, { tabId: 'tab-welcome', threadId: 'welcome:/workspace/test-root', workspacePath: '/workspace/test-root', initialUrl: 'http://localhost:5173/' })
    await manager.createTab(win, { tabId: 'tab-other', threadId: 'thread-other', workspacePath: '/workspace/test-root', initialUrl: 'http://localhost:5173/' })
    manager.rebindThread(win, 'welcome:/workspace/test-root', 'thread-new')
    expect(manager.listAutomationTargetTabs(win, 'thread-new').map(snapshot => snapshot.tabId)).toEqual(['tab-welcome'])
    expect(manager.listAutomationTargetTabs(win, 'thread-other').map(snapshot => snapshot.tabId)).toEqual(['tab-other'])
    expect(manager.listAutomationTargetTabs(win, 'welcome:/workspace/test-root')).toEqual([])
  })
})

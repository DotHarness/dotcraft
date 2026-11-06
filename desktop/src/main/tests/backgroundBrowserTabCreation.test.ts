import { beforeEach, expect, it, vi } from 'vitest'
import type { BrowserSnapshot } from '../viewerBrowser'
import type { WindowIpcContext } from '../ipcBridgeContracts'

const host = vi.hoisted(() => ({ create: vi.fn(), window: null as unknown }))
vi.mock('electron', () => ({ app: {}, BrowserWindow: { fromWebContents: () => host.window }, session: {} }))
vi.mock('../viewerBrowser', () => ({ viewerBrowserManager: { createTab: host.create }, partitionForWorkspace: vi.fn() }))
vi.mock('../browserUseManager', () => ({ browserUseManager: {} }))
vi.mock('../browserFeedbackIpc', () => ({ registerBrowserFeedbackIpc: vi.fn() }))
vi.mock('../viewerTerminalIpc', () => ({ registerViewerTerminalHandlers: vi.fn() }))
vi.mock('../projectActions/projectActionsIpc', () => ({ registerProjectActionsHandlers: () => () => {} }))
vi.mock('../chromeSetup', () => ({ checkChromeSetup: vi.fn(), installChromeNativeHost: vi.fn(), openChromeWindow: vi.fn() }))

import { registerViewerBrowserHandlers } from '../ipcViewerBrowser'
import { handleBrowserEvent } from '../../renderer/utils/browserEventHandler'
import { useDetailPanelStore } from '../../renderer/stores/detailPanelStore'

type InvokeHandler = WindowIpcContext['handleSafe'] extends (channel: string, listener: infer T) => void ? T : never
const routes = new Map<string, InvokeHandler>()
const foreground = '/project-b'

beforeEach(() => {
  vi.clearAllMocks()
  routes.clear()
  const owner = { isDestroyed: () => false, webContents: { id: 10 } }
  host.window = owner
  host.create.mockImplementation(async (_window, params) => ({ ...params, currentUrl: params.initialUrl,
    title: 'Child', canGoBack: false, canGoForward: false, loading: false, suspended: false }))
  registerViewerBrowserHandlers({
    owner, workspacePath: foreground,
    handleSafe: (name: string, handler: InvokeHandler) => routes.set(name, handler),
    callbacks: { getSettings: () => ({ locale: 'en' }), getWorkspaceProjects: () => ({
      foregroundWorkspacePath: foreground, foregroundProjectId: foreground, secondaryLimit: 8,
      projects: [{ kind: 'local', path: '/project-a', projectId: '/project-a' },
        { kind: 'local', path: foreground, projectId: foreground },
        { kind: 'remote', path: '/remote', projectId: 'remote:one' }]
    }) }
  } as unknown as WindowIpcContext)
  useDetailPanelStore.getState().reset()
})

it('opens a retained page child through the real renderer-to-main authorization path without changing the foreground panel', async () => {
  const store = useDetailPanelStore.getState()
  store.onWorkspaceSwitched('/project-a')
  store.onThreadSwitched('thread-a')
  const source = store.openBrowser({ threadId: 'thread-a' })
  store.onWorkspaceSwitched(foreground)
  store.onThreadSwitched('thread-b')
  store.setActiveDetailTab('plan')
  let created!: Promise<BrowserSnapshot>
  Object.defineProperty(globalThis, 'window', { configurable: true, value: { api: { workspace: { viewer: { browser: {
    create(params: unknown) {
      created = Promise.resolve(routes.get('viewer:browser:create')!({ sender: {} } as Electron.IpcMainInvokeEvent, params))
      return created
    }
  } } } } } })
  try {
    handleBrowserEvent({ type: 'request-new-tab', tabId: source, threadId: 'thread-a', projectKey: '/project-a',
      url: 'https://example.test/child' }, { locale: 'en', workspacePath: foreground })
    const snapshot = await created
    expect(snapshot.currentUrl).toBe('https://example.test/child')
    expect(host.create).toHaveBeenCalledWith(host.window, expect.objectContaining({
      projectKey: '/project-a', workspacePath: '/project-a', threadId: 'thread-a', restore: 'none'
    }))
    expect(store.getPanel('thread-a', '/project-a').tabs).toHaveLength(2)
    expect(store.getCurrentPanel().activeDetailTab).toEqual({ kind: 'system', id: 'plan' })
    expect(store.getCurrentScope()).toEqual({ projectKey: foreground, threadId: 'thread-b' })
  } finally {
    Reflect.deleteProperty(globalThis, 'window')
  }
})

it('rejects mismatched roots, unknown projects and background remote scopes before creating any page', async () => {
  for (const scope of [
    { workspacePath: '/outside', projectKey: '/project-a' },
    { workspacePath: '/project-a', projectKey: '/unknown' },
    { workspacePath: '/remote', projectKey: 'remote:one' },
    { workspacePath: '/outside', projectKey: foreground }
  ]) {
    await expect(routes.get('viewer:browser:create')!({ sender: {} } as Electron.IpcMainInvokeEvent,
      { tabId: 'forged', threadId: 'thread-a', restore: 'none', ...scope })).rejects.toThrow()
  }
  expect(host.create).not.toHaveBeenCalled()
})

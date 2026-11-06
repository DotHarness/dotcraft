import { beforeEach, describe, expect, it, vi } from 'vitest'
import { EventEmitter } from 'node:events'
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const electron = vi.hoisted(() => ({ sessions: new Map<string, unknown>(), fromPartition: vi.fn() }))
vi.mock('electron', () => ({
  app: { getName: () => 'DotCraft', getPreferredSystemLanguages: () => ['en'], getLocale: () => 'en' },
  session: { fromPartition: electron.fromPartition },
  protocol: { registerSchemesAsPrivileged: vi.fn() },
  net: { fetch: async (url: string) => new Response(await readFile(fileURLToPath(url))) },
  BrowserWindow: { fromWebContents: () => null },
  nativeImage: { createFromBuffer: () => ({ isEmpty: () => true }) },
  shell: { openExternal: vi.fn(), openPath: vi.fn() }
}))

import { ViewerBrowserManager, partitionForWorkspace } from '../viewerBrowser'
import { buildViewerUrl, authorizeViewerFile } from '../viewerFileProtocol'
import { registerWindowFileAccess } from '../windowFileAccess'

function makeSession() {
  const handlers = new Map<string, (request: Request) => Promise<Response>>()
  return {
    handlers,
    protocol: { handle: vi.fn((scheme: string, handler: (request: Request) => Promise<Response>) => handlers.set(scheme, handler)) },
    on: vi.fn(), getUserAgent: () => 'Mozilla/5.0 Chrome/148 Safari/537.36', setUserAgent: vi.fn(),
    setPermissionCheckHandler: vi.fn(), setPermissionRequestHandler: vi.fn(),
    webRequest: { onBeforeSendHeaders: vi.fn(), onBeforeRequest: vi.fn() }
  }
}
function sessionFor(partition: string): ReturnType<typeof makeSession> {
  let target = electron.sessions.get(partition) as ReturnType<typeof makeSession> | undefined
  if (!target) { target = makeSession(); electron.sessions.set(partition, target) }
  return target
}
function makePage(target: unknown) {
  const emitter = new EventEmitter()
  let url = 'about:blank'
  return Object.assign(emitter, {
    session: target, isDestroyed: () => false, isLoading: () => false, getURL: () => url,
    isCurrentlyAudible: () => false, isBeingCaptured: () => false,
    getUserAgent: () => 'Mozilla/5.0 Chrome/148 Safari/537.36', getTitle: () => 'Test',
    setWindowOpenHandler: vi.fn(), loadURL: vi.fn(async (next: string) => { url = next }),
    navigationHistory: { canGoBack: () => false, canGoForward: () => false }, close: vi.fn()
  }) as unknown as Electron.WebContents & { loadURL: ReturnType<typeof vi.fn>; setWindowOpenHandler: ReturnType<typeof vi.fn> }
}
function harness() {
  const manager = new ViewerBrowserManager()
  const pages = new Map<string, ReturnType<typeof makePage>>()
  const requested = new Map<number, Set<string>>()
  vi.spyOn(manager.hosts, 'request').mockImplementation(async (win, tabId, partition) => {
    const owned = requested.get(win.id) ?? new Set<string>()
    owned.add(tabId); requested.set(win.id, owned)
    const page = makePage(sessionFor(partition)); pages.set(tabId, page)
    return page
  })
  vi.spyOn(manager.hosts, 'list').mockImplementation(win => [...requested.get(win.id) ?? []].map(tabId => ({ tabId }) as never))
  const window = (id: number) => ({
    id, isDestroyed: () => false, webContents: { id: id * 10, isDestroyed: () => false, send: vi.fn() }
  }) as unknown as Electron.BrowserWindow & { webContents: { send: ReturnType<typeof vi.fn> } }
  return { manager, pages, a: window(1), b: window(2) }
}
beforeEach(() => { electron.sessions.clear(); electron.fromPartition.mockImplementation(sessionFor) })

describe('browser window session ownership', () => {
  it('shares website profiles across windows while denying all local-file request schemes', async () => {
    const { manager, pages, a, b } = harness()
    await manager.createTab(a, { tabId: 'a', workspacePath: '/work', initialUrl: 'https://example.com' })
    await manager.createTab(b, { tabId: 'b', workspacePath: '/work', initialUrl: 'https://example.com' })
    const profile = sessionFor(partitionForWorkspace('/work'))
    expect(pages.get('a')!.session).toBe(pages.get('b')!.session)
    expect(profile.handlers.size).toBe(0)
    const [, beforeRequest] = profile.webRequest.onBeforeRequest.mock.calls[0]
    for (const url of ['file:///private/key', 'dotcraft-viewer://workspace/private/key', 'dotcraft-plugin://plugin/private']) {
      const callback = vi.fn(); beforeRequest({ url, resourceType: 'subFrame' }, callback)
      expect(callback).toHaveBeenCalledWith({ cancel: true })
    }
  })

  it('uses the real automation target to select a persistent profile despite starting about:blank', async () => {
    const { manager, pages, a } = harness()
    await manager.createAutomationTab(a, { tabId: 'auto', workspacePath: '/work', initialUrl: 'about:blank', sessionUrl: 'http://localhost:3000', allowFileScheme: true })
    expect(pages.get('auto')!.session).toBe(sessionFor(partitionForWorkspace('/work')))
    await manager.loadAutomationUrl(a, { tabId: 'auto', url: 'http://localhost:3000/' })
    expect(pages.get('auto')!.loadURL).toHaveBeenCalledWith('http://localhost:3000/')
    await expect(manager.loadAutomationUrl(a, { tabId: 'auto', url: 'file:///private/key' })).rejects.toThrow('Create a new tab')
  })

  it('opens cross-category UI navigation separately and leaves the source page unchanged', async () => {
    const { manager, pages, a } = harness()
    await manager.createTab(a, { tabId: 'site', workspacePath: '/work', initialUrl: 'https://example.com' })
    const source = pages.get('site')!
    source.loadURL.mockClear()
    const local = buildViewerUrl('/work/index.html')
    await manager.navigate(a, { tabId: 'site', url: local })
    expect(source.loadURL).not.toHaveBeenCalled()
    expect(a.webContents.send).toHaveBeenCalledWith('viewer:browser:event', expect.objectContaining({ type: 'request-new-tab', url: local }))
    const frame = { url: local, isMainFrame: false, preventDefault: vi.fn() }
    ;(source as unknown as EventEmitter).emit('will-frame-navigate', frame)
    expect(frame.preventDefault).toHaveBeenCalledOnce()
  })

  it('isolates local preview grants by owner and translates file inputs through the viewer handler', async () => {
    const root = await mkdtemp(join(tmpdir(), 'dotcraft-browser-owners-'))
    const workspace = join(root, 'work'); await mkdir(workspace)
    const local = join(workspace, 'index.html'); await writeFile(local, '<h1>Local</h1>')
    const outside = join(root, 'external.html'); await writeFile(outside, 'External')
    const { manager, pages, a, b } = harness()
    const releaseA = registerWindowFileAccess(a.webContents.id, { getWorkspaceRoot: () => workspace })
    const releaseB = registerWindowFileAccess(b.webContents.id, { getWorkspaceRoot: () => workspace })
    try {
      await manager.createAutomationTab(a, { tabId: 'local-a', workspacePath: workspace, initialUrl: 'about:blank', sessionUrl: pathToFileURL(local).href, allowFileScheme: true })
      await manager.createTab(b, { tabId: 'local-b', workspacePath: workspace, initialUrl: buildViewerUrl(local) })
      expect(pages.get('local-a')!.session).not.toBe(pages.get('local-b')!.session)
      const sessionA = pages.get('local-a')!.session as unknown as ReturnType<typeof makeSession>
      const sessionB = pages.get('local-b')!.session as unknown as ReturnType<typeof makeSession>
      await manager.loadAutomationUrl(a, { tabId: 'local-a', url: pathToFileURL(local).href })
      expect(pages.get('local-a')!.loadURL).toHaveBeenCalledWith(buildViewerUrl(local))
      const handlerA = sessionA.handlers.get('dotcraft-viewer')!
      const handlerB = sessionB.handlers.get('dotcraft-viewer')!
      expect((await handlerA(new Request(buildViewerUrl(local)))).status).toBe(200)
      expect((await handlerA(new Request(buildViewerUrl(outside)))).status).toBe(403)
      await authorizeViewerFile(outside, a.webContents.id)
      expect((await handlerA(new Request(buildViewerUrl(outside)))).status).toBe(200)
      expect((await handlerB(new Request(buildViewerUrl(outside)))).status).toBe(403)
      const source = pages.get('local-a')!; source.loadURL.mockClear()
      await manager.navigate(a, { tabId: 'local-a', url: 'https://example.com/' })
      expect(source.loadURL).not.toHaveBeenCalled()
      expect(a.webContents.send).toHaveBeenCalledWith('viewer:browser:event', expect.objectContaining({ type: 'request-new-tab', url: 'https://example.com/' }))
    } finally { releaseA(); releaseB(); await rm(root, { recursive: true, force: true }) }
  })
})

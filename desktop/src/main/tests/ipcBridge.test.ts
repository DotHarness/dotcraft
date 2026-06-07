import { describe, it, expect, vi, beforeEach } from 'vitest'
import { ipcMain, Notification, shell } from 'electron'
import { promises as fs } from 'fs'
import * as path from 'path'

const {
  scanModulesMock,
  moduleProcessManagerStartMock,
  detectEditorsMock,
  launchEditorMock,
  execFileMock,
  existsSyncMock,
  listWorkspaceFilesMock,
  notificationShowMock
} = vi.hoisted(() => ({
  scanModulesMock: vi.fn(),
  moduleProcessManagerStartMock: vi.fn(),
  detectEditorsMock: vi.fn(),
  launchEditorMock: vi.fn(),
  execFileMock: vi.fn(),
  existsSyncMock: vi.fn(),
  listWorkspaceFilesMock: vi.fn(),
  notificationShowMock: vi.fn()
}))

vi.mock('fs', () => ({
  existsSync: existsSyncMock,
  promises: {
    readFile: vi.fn(),
    writeFile: vi.fn(),
    stat: vi.fn(),
    realpath: vi.fn(),
    mkdir: vi.fn(),
    rm: vi.fn(),
    rename: vi.fn(),
    readdir: vi.fn().mockResolvedValue([])
  }
}))

vi.mock('child_process', () => ({
  execFile: execFileMock
}))

vi.mock('electron', () => {
  const NotificationMock = vi.fn(function (this: { show: () => void }, _options: unknown) {
    this.show = notificationShowMock
  })
  Object.assign(NotificationMock, {
    isSupported: vi.fn(() => false)
  })
  return {
  app: {
    isPackaged: true,
    getPath: vi.fn(() => 'C:\\Users\\tester'),
    getAppPath: vi.fn(() => 'C:\\sample\\desktop-app'),
    getApplicationNameForProtocol: vi.fn((url: string) => url.startsWith('oratorio://') ? 'Oratorio' : '')
  },
  ipcMain: {
    handle: vi.fn(),
    removeHandler: vi.fn()
  },
  BrowserWindow: {
    getAllWindows: vi.fn(() => []),
    getFocusedWindow: vi.fn(() => null)
  },
  dialog: {
    showOpenDialog: vi.fn()
  },
  Notification: NotificationMock,
  shell: {
    openExternal: vi.fn().mockResolvedValue(undefined),
    openPath: vi.fn().mockResolvedValue(''),
    showItemInFolder: vi.fn()
  }
}})

vi.mock('../moduleScanner', async () => {
  const actual = await vi.importActual('../moduleScanner')
  return {
    ...actual,
    scanModules: scanModulesMock
  }
})

vi.mock('../moduleProcessManager', async () => {
  const actual = await vi.importActual('../moduleProcessManager')
  class MockModuleProcessManager {
    start = moduleProcessManagerStartMock
    stop = vi.fn()
    stopAll = vi.fn().mockResolvedValue(undefined)
    getStatusMap = vi.fn(() => ({}))
    autoStartModules = vi.fn().mockResolvedValue(undefined)
    getRecentLogs = vi.fn(() => [])
    getQrStatus = vi.fn(() => ({ active: false, qrDataUrl: null }))
  }
  return {
    ...actual,
    ModuleProcessManager: MockModuleProcessManager
  }
})

vi.mock('../externalEditors', () => ({
  detectEditors: detectEditorsMock,
  launchEditor: launchEditorMock
}))

vi.mock('../workspaceComposerIpc', async () => {
  const actual = await vi.importActual('../workspaceComposerIpc')
  return {
    ...actual,
    activateFileIndexWorkspace: vi.fn(),
    cleanupWorkspaceCache: vi.fn().mockResolvedValue(undefined),
    listWorkspaceFiles: listWorkspaceFilesMock,
    warmFileSearchIndex: vi.fn()
  }
})

import {
  createServerRequestBridge,
  registerIpcHandlers,
  unregisterIpcHandlers,
  sanitizeHttpOrHttpsUrl,
  sanitizeExternalUrl,
  openExternalUrl,
  openExternalHttpUrl,
  openAppHandoffUrl,
  fetchDesktopExtensionJson,
  postDesktopExtensionJson,
  getProtocolHandlerName,
  broadcastNotification,
  broadcastServerRequest,
  shouldShowTaskCompletionNotification,
  getRemoteServersManager
} from '../ipcBridge'

type IpcCallbacks = NonNullable<Parameters<typeof registerIpcHandlers>[3]>
type ExecFileCallback = (
  error: (Error & { code?: number | string }) | null,
  stdout: string,
  stderr: string
) => void

function createIpcCallbacks(overrides: Partial<IpcCallbacks> = {}): IpcCallbacks {
  return {
    onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
    onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
    onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
    onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
    onOpenNewWindow: vi.fn(),
    onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
    getSettings: vi.fn(() => ({ locale: 'en' })),
    updateSettings: vi.fn(),
    getRecentWorkspaces: vi.fn(() => []),
    getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
    getWorkspaceStatus: vi.fn(() => ({ status: 'ready', workspacePath: '/workspace', hasUserConfig: false, providers: [] })),
    ...overrides
  }
}

function registerHandlersForTest(
  workspacePath = '/workspace',
  getWireClient: Parameters<typeof registerIpcHandlers>[1] = () => null,
  callbacks: IpcCallbacks = createIpcCallbacks()
): Map<string, (...args: unknown[]) => unknown> {
  const handlers = new Map<string, (...args: unknown[]) => unknown>()
  vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
    handlers.set(channel, handler as (...args: unknown[]) => unknown)
  })
  registerIpcHandlers(null, getWireClient, workspacePath, callbacks)
  return handlers
}

function gitError(exitCode: number, message = ''): Error & { code: number } {
  return Object.assign(new Error(message), { code: exitCode })
}

function mockGitCommands(
  resolver: (args: string[]) => { error?: Error & { code?: number | string }; stdout?: string; stderr?: string }
): void {
  execFileMock.mockImplementation((_command, args, _options, callback: ExecFileCallback) => {
    const result = resolver(args as string[])
    callback(result.error ?? null, result.stdout ?? '', result.stderr ?? '')
    return null
  })
}

function mockDesktopExtensionPluginFixture(options: {
  rootPath?: string
  extension?: Record<string, unknown>
  apps?: unknown[]
} = {}): string {
  const rootPath = path.resolve(options.rootPath ?? '/plugins/oratorio')
  const manifestPath = path.join(rootPath, '.craft-plugin', 'plugin.json')
  const desktopExtensionsPath = path.join(rootPath, 'desktop-extensions.json')
  const appsPath = path.join(rootPath, 'apps.json')
  const files = new Map<string, string>([
    [manifestPath, JSON.stringify({
      schemaVersion: 1,
      id: 'oratorio',
      desktopExtensions: './desktop-extensions.json',
      apps: './apps.json'
    })],
    [desktopExtensionsPath, JSON.stringify({
      extensions: [
        {
          id: 'oratorio-board',
          entry: './desktop/oratorio-board.mjs',
          requiredAppIds: ['com.dotharness.oratorio'],
          connectOrigins: ['http://127.0.0.1:*'],
          surfaceWriteScopes: ['board.manage'],
          ...(options.extension ?? {})
        }
      ]
    })],
    [appsPath, JSON.stringify({
      apps: options.apps ?? [
        {
          appId: 'com.dotharness.oratorio',
          nativeApplication: {
            protocol: 'oratorio',
            platforms: {
              windows: { protocol: 'oratorio' }
            }
          }
        }
      ]
    })]
  ])

  vi.mocked(fs.realpath).mockImplementation(async (target) => path.resolve(String(target)))
  vi.mocked(fs.stat).mockResolvedValue({ isFile: () => true } as Awaited<ReturnType<typeof fs.stat>>)
  vi.mocked(fs.readFile).mockImplementation(async (target) => {
    const content = files.get(path.resolve(String(target)))
    if (content == null) {
      throw Object.assign(new Error(`ENOENT: ${target}`), { code: 'ENOENT' })
    }
    return content
  })
  return rootPath
}

// ---------------------------------------------------------------------------
// ipcBridge — server-request bridge tests
//
// The bridge creates a pending Promise per request (identified by bridgeId),
// which resolves when the Renderer sends back a response via
// appserver:server-response. These tests verify the pending-map logic directly
// (without standing up a real Electron IPC environment).
// ---------------------------------------------------------------------------

describe('createServerRequestBridge', () => {
  it('returns a unique bridgeId for each call', () => {
    const a = createServerRequestBridge()
    const b = createServerRequestBridge()
    expect(a.bridgeId).not.toBe(b.bridgeId)
  })

  it('returns a promise that is pending until resolved externally', async () => {
    const { promise } = createServerRequestBridge()
    let settled = false
    void promise.then(() => { settled = true })
    await new Promise((r) => setTimeout(r, 10))
    expect(settled).toBe(false)
  })

  it('bridge IDs are numeric strings in ascending order', () => {
    const ids = [
      createServerRequestBridge().bridgeId,
      createServerRequestBridge().bridgeId,
      createServerRequestBridge().bridgeId
    ]
    const nums = ids.map(Number)
    expect(nums[0]).toBeLessThan(nums[1])
    expect(nums[1]).toBeLessThan(nums[2])
  })
})

describe('sanitizeHttpOrHttpsUrl', () => {
  it('accepts http and https URLs and returns normalized href', () => {
    expect(sanitizeHttpOrHttpsUrl('http://127.0.0.1:8080/dashboard')).toBe(
      'http://127.0.0.1:8080/dashboard'
    )
    expect(sanitizeHttpOrHttpsUrl('https://example.com/path')).toBe('https://example.com/path')
  })

  it('returns null for empty, whitespace-only, or undefined', () => {
    expect(sanitizeHttpOrHttpsUrl(undefined)).toBeNull()
    expect(sanitizeHttpOrHttpsUrl('')).toBeNull()
    expect(sanitizeHttpOrHttpsUrl('   ')).toBeNull()
  })

  it('returns null for non-http(s) protocols', () => {
    expect(sanitizeHttpOrHttpsUrl('file:///etc/passwd')).toBeNull()
    expect(sanitizeHttpOrHttpsUrl('ms-msdt:foo')).toBeNull()
    expect(sanitizeHttpOrHttpsUrl('custom:host')).toBeNull()
  })

  it('returns null for malformed strings', () => {
    expect(sanitizeHttpOrHttpsUrl('not a url')).toBeNull()
  })
})

describe('openExternalHttpUrl', () => {
  it('throws Invalid URL for empty input', async () => {
    await expect(openExternalHttpUrl('')).rejects.toThrow('Invalid URL')
  })

  it('throws Only http(s) URLs are allowed for disallowed protocols', async () => {
    await expect(openExternalHttpUrl('file:///tmp/x')).rejects.toThrow('Only http(s) URLs are allowed')
  })

  it('calls shell.openExternal with sanitized href for https URL', async () => {
    vi.mocked(shell.openExternal).mockClear()
    await openExternalHttpUrl('https://example.com')
    expect(shell.openExternal).toHaveBeenCalledTimes(1)
    expect(shell.openExternal).toHaveBeenCalledWith('https://example.com/')
  })
})

describe('sanitizeExternalUrl', () => {
  it('accepts http(s), mailto, and tel URLs', () => {
    expect(sanitizeExternalUrl('https://example.com')).toBe('https://example.com/')
    expect(sanitizeExternalUrl('mailto:test@example.com')).toBe('mailto:test@example.com')
    expect(sanitizeExternalUrl('tel:+123456789')).toBe('tel:+123456789')
  })

  it('rejects unsupported schemes and overlong values', () => {
    expect(sanitizeExternalUrl('file:///tmp/x')).toBeNull()
    expect(sanitizeExternalUrl(`https://example.com/${'a'.repeat(5000)}`)).toBeNull()
  })
})

describe('openExternalUrl', () => {
  it('allows mailto URLs', async () => {
    vi.mocked(shell.openExternal).mockClear()
    await openExternalUrl('mailto:test@example.com')
    expect(shell.openExternal).toHaveBeenCalledWith('mailto:test@example.com')
  })

  it('allows app deep link URLs', async () => {
    vi.mocked(shell.openExternal).mockClear()
    await openExternalUrl('oratorio://dotcraft/connect?request=req_1')
    expect(shell.openExternal).toHaveBeenCalledWith('oratorio://dotcraft/connect?request=req_1')
  })

  it('rejects unsupported schemes', async () => {
    await expect(openExternalUrl('file:///tmp/x')).rejects.toThrow(
      'URL scheme is not allowed'
    )
  })
})

describe('openAppHandoffUrl', () => {
  it('invokes loopback HTTP handoffs without opening the browser', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200 })
    vi.stubGlobal('fetch', fetchMock)
    vi.mocked(shell.openExternal).mockClear()

    await openAppHandoffUrl('http://127.0.0.1:39777/dotcraft/bind?app=com.dotharness.dotcraft-unity')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(shell.openExternal).not.toHaveBeenCalled()
    vi.unstubAllGlobals()
  })

  it('falls back to the OS handler for custom protocol handoffs', async () => {
    vi.mocked(shell.openExternal).mockClear()

    await openAppHandoffUrl('oratorio://dotcraft/bind?request=req_1')

    expect(shell.openExternal).toHaveBeenCalledWith('oratorio://dotcraft/bind?request=req_1')
  })
})

describe('fetchDesktopExtensionJson', () => {
  it('allows declared wildcard loopback origins', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      text: vi.fn().mockResolvedValue('{"items":[]}')
    })
    vi.stubGlobal('fetch', fetchMock)

    await expect(fetchDesktopExtensionJson(
      'http://127.0.0.1:5087/api/v1/items',
      { connectOrigins: ['http://127.0.0.1:*'] }
    )).resolves.toEqual({ items: [] })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect((fetchMock.mock.calls[0]![1] as { redirect: string }).redirect).toBe('error')
    vi.unstubAllGlobals()
  })

  it('rejects undeclared origins before fetching', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    await expect(fetchDesktopExtensionJson(
      'http://127.0.0.1:5087/api/v1/items',
      { connectOrigins: ['http://localhost:*'] }
    )).rejects.toThrow('not allowed')

    expect(fetchMock).not.toHaveBeenCalled()
    vi.unstubAllGlobals()
  })
})

describe('postDesktopExtensionJson', () => {
  it('issues a POST with a JSON body to a declared loopback origin', async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      text: vi.fn().mockResolvedValue('{"ok":true}')
    })
    vi.stubGlobal('fetch', fetchMock)

    await expect(postDesktopExtensionJson(
      'http://127.0.0.1:5087/api/v1/items/abc/dispatch',
      { connectOrigins: ['http://127.0.0.1:*'], surfaceWriteScopes: ['board.manage'] },
      { reason: 'manual' }
    )).resolves.toEqual({ ok: true })

    expect(fetchMock).toHaveBeenCalledTimes(1)
    const init = fetchMock.mock.calls[0]![1] as { method: string; headers: Record<string, string>; body: string }
    expect(init.method).toBe('POST')
    expect((init as { redirect: string }).redirect).toBe('error')
    expect(init.headers).toMatchObject({ 'Content-Type': 'application/json' })
    expect(JSON.parse(init.body)).toEqual({ reason: 'manual' })
    vi.unstubAllGlobals()
  })

  it('rejects undeclared origins before posting', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    await expect(postDesktopExtensionJson(
      'http://127.0.0.1:5087/api/v1/items/abc/dispatch',
      { connectOrigins: ['http://localhost:*'], surfaceWriteScopes: ['board.manage'] },
      { reason: 'manual' }
    )).rejects.toThrow('not allowed')

    expect(fetchMock).not.toHaveBeenCalled()
    vi.unstubAllGlobals()
  })

  it('rejects writes when the extension did not declare surfaceWriteScopes', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)

    await expect(postDesktopExtensionJson(
      'http://127.0.0.1:5087/api/v1/items/abc/dispatch',
      { connectOrigins: ['http://127.0.0.1:*'] },
      { reason: 'manual' }
    )).rejects.toThrow('surfaceWriteScopes')

    expect(fetchMock).not.toHaveBeenCalled()
    vi.unstubAllGlobals()
  })
})

describe('getProtocolHandlerName', () => {
  it('queries the OS protocol handler name', () => {
    expect(getProtocolHandlerName('oratorio')).toBe('Oratorio')
  })
})

describe('registerIpcHandlers', () => {
  beforeEach(async () => {
    vi.clearAllMocks()
    scanModulesMock.mockResolvedValue([])
    moduleProcessManagerStartMock.mockResolvedValue({ ok: true })
    detectEditorsMock.mockResolvedValue([
      { id: 'cursor', labelKey: 'editors.cursor', iconKey: 'editor-generic' },
      { id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' }
    ])
    launchEditorMock.mockResolvedValue(undefined)
    listWorkspaceFilesMock.mockResolvedValue({
      files: [],
      indexStatus: 'ready',
      indexedCount: 0,
      stale: false
    })
    existsSyncMock.mockReturnValue(true)
  })

  it('desktop extension network requests use the main-side descriptor grant policy', async () => {
    const rootPath = mockDesktopExtensionPluginFixture()
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      text: vi.fn().mockResolvedValue('{"items":[]}')
    })
    vi.stubGlobal('fetch', fetchMock)
    const handlers = registerHandlersForTest()

    const grant = await handlers.get('desktop-extension:authorize-extension')?.({}, {
      pluginId: 'oratorio',
      rootPath,
      extensionId: 'oratorio-board'
    }) as { grantId: string }

    await expect(handlers.get('desktop-extension:fetch-json')?.({}, {
      grantId: grant.grantId,
      url: 'http://127.0.0.1:5087/api/v1/items',
      connectOrigins: ['http://localhost:*']
    })).resolves.toEqual({ items: [] })

    await expect(handlers.get('desktop-extension:fetch-json')?.({}, {
      grantId: grant.grantId,
      url: 'http://localhost:5087/api/v1/items',
      connectOrigins: ['http://localhost:*']
    })).rejects.toThrow('not allowed')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect((fetchMock.mock.calls[0]![1] as { redirect: string }).redirect).toBe('error')
    vi.unstubAllGlobals()
  })

  it('forwards workspace:remove-recent to the main callback', async () => {
    const removeRecentWorkspace = vi.fn()
    const handlers = registerHandlersForTest('/workspace', () => null, createIpcCallbacks({
      removeRecentWorkspace
    }))

    await handlers.get('workspace:remove-recent')?.({}, '/workspace/other')

    expect(removeRecentWorkspace).toHaveBeenCalledWith('/workspace/other')
  })

  it('forwards workspace:disconnect-remote to the main callback', async () => {
    const onDisconnectRemoteProject = vi.fn().mockResolvedValue(undefined)
    const handlers = registerHandlersForTest('/workspace', () => null, createIpcCallbacks({
      onDisconnectRemoteProject
    }))

    await handlers.get('workspace:disconnect-remote')?.({})

    expect(onDisconnectRemoteProject).toHaveBeenCalledTimes(1)
  })

  it('observes successful AppServer requests after forwarding them', async () => {
    const result = { subscribed: true }
    const client = {
      sendRequest: vi.fn().mockResolvedValue(result)
    }
    const onAppServerRequestCompleted = vi.fn()
    const handlers = registerHandlersForTest('/workspace', () => client as never, createIpcCallbacks({
      onAppServerRequestCompleted
    }))
    const params = { threadId: 'thread_1' }

    await expect(
      handlers.get('appserver:send-request')?.({}, 'thread/subscribe', params, 20_000)
    ).resolves.toBe(result)

    expect(client.sendRequest).toHaveBeenCalledWith('thread/subscribe', params, 20_000)
    expect(onAppServerRequestCompleted).toHaveBeenCalledWith(
      client,
      'thread/subscribe',
      params,
      result
    )
  })

  it('desktop extension POST requires descriptor surfaceWriteScopes', async () => {
    const rootPath = mockDesktopExtensionPluginFixture({
      extension: { surfaceWriteScopes: [] }
    })
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    const handlers = registerHandlersForTest()

    const grant = await handlers.get('desktop-extension:authorize-extension')?.({}, {
      pluginId: 'oratorio',
      rootPath,
      extensionId: 'oratorio-board'
    }) as { grantId: string }

    await expect(handlers.get('desktop-extension:post-json')?.({}, {
      grantId: grant.grantId,
      url: 'http://127.0.0.1:5087/api/v1/items',
      body: {}
    })).rejects.toThrow('surfaceWriteScopes')

    expect(fetchMock).not.toHaveBeenCalled()
    vi.unstubAllGlobals()
  })

  it('desktop extension app binding IPC is scoped by requiredAppIds', async () => {
    const rootPath = mockDesktopExtensionPluginFixture()
    const sendRequest = vi.fn().mockResolvedValue({ appId: 'com.dotharness.oratorio', state: 'connected' })
    const handlers = registerHandlersForTest('/workspace', () => ({ sendRequest } as never))

    const grant = await handlers.get('desktop-extension:authorize-extension')?.({}, {
      pluginId: 'oratorio',
      rootPath,
      extensionId: 'oratorio-board'
    }) as { grantId: string }

    await expect(handlers.get('desktop-extension:app-connection-status')?.({}, {
      grantId: grant.grantId,
      appId: 'com.dotharness.oratorio'
    })).resolves.toEqual({ appId: 'com.dotharness.oratorio', state: 'connected' })

    await expect(handlers.get('desktop-extension:app-connection-start')?.({}, {
      grantId: grant.grantId,
      appId: 'com.example.other'
    })).rejects.toThrow('not allowed')

    expect(sendRequest).toHaveBeenCalledWith(
      'app/connection/status',
      { appId: 'com.dotharness.oratorio' },
      20_000
    )
  })

  it('desktop extension app-open requires the owning app native protocol', async () => {
    const rootPath = mockDesktopExtensionPluginFixture()
    const handlers = registerHandlersForTest()

    const grant = await handlers.get('desktop-extension:authorize-extension')?.({}, {
      pluginId: 'oratorio',
      rootPath,
      extensionId: 'oratorio-board'
    }) as { grantId: string }

    await expect(handlers.get('desktop-extension:app-open')?.({}, {
      grantId: grant.grantId,
      appId: 'com.dotharness.oratorio',
      url: 'https://example.com/open'
    })).rejects.toThrow('not allowed')

    await handlers.get('desktop-extension:app-open')?.({}, {
      grantId: grant.grantId,
      appId: 'com.dotharness.oratorio',
      url: 'oratorio://open/board'
    })

    expect(shell.openExternal).toHaveBeenCalledWith('oratorio://open/board')
  })

  it('desktop extension grants are invalidated when IPC handlers unregister', async () => {
    const rootPath = mockDesktopExtensionPluginFixture()
    const handlers = registerHandlersForTest()
    const grant = await handlers.get('desktop-extension:authorize-extension')?.({}, {
      pluginId: 'oratorio',
      rootPath,
      extensionId: 'oratorio-board'
    }) as { grantId: string }

    unregisterIpcHandlers()

    await expect(handlers.get('desktop-extension:fetch-json')?.({}, {
      grantId: grant.grantId,
      url: 'http://127.0.0.1:5087/api/v1/items'
    })).rejects.toThrow('grant')
  })

  it('desktop extension grants can be explicitly revoked', async () => {
    const rootPath = mockDesktopExtensionPluginFixture()
    const handlers = registerHandlersForTest()
    const grant = await handlers.get('desktop-extension:authorize-extension')?.({}, {
      pluginId: 'oratorio',
      rootPath,
      extensionId: 'oratorio-board'
    }) as { grantId: string }

    await expect(handlers.get('desktop-extension:revoke-extension')?.({}, {
      grantId: grant.grantId
    })).resolves.toEqual({ ok: true })

    await expect(handlers.get('desktop-extension:fetch-json')?.({}, {
      grantId: grant.grantId,
      url: 'http://127.0.0.1:5087/api/v1/items'
    })).rejects.toThrow('grant')
  })

  it('git:commit filters missing and ignored paths before staging and committing', async () => {
    mockGitCommands((args) => {
      if (args[0] === 'status') {
        return { stdout: ' M src/valid.ts\0' }
      }
      if (args[0] === 'diff') {
        return { error: gitError(1) }
      }
      if (args[0] === 'commit') {
        return { stdout: '[main abc123] fix: valid\n 1 file changed\n' }
      }
      return { stdout: '' }
    })
    const handlers = registerHandlersForTest()
    const commit = handlers.get('git:commit')!

    const result = await commit(
      {},
      '/workspace',
      ['src/valid.ts', 'server/internal/distribution/db_migrations.go', 'ignored/generated.log'],
      'fix: valid'
    )

    expect(result).toBe('[main abc123] fix: valid\n 1 file changed')
    const gitCalls = execFileMock.mock.calls.map(([, args]) => args as string[])
    expect(gitCalls).toEqual([
      ['status', '--porcelain=v1', '-z', '--untracked-files=all', '--', 'src/valid.ts', 'server/internal/distribution/db_migrations.go', 'ignored/generated.log'],
      ['add', '--', 'src/valid.ts'],
      ['diff', '--cached', '--quiet', '--', 'src/valid.ts'],
      ['commit', '-m', 'fix: valid', '--', 'src/valid.ts']
    ])
  })

  it('git:commit skips add and commit when every requested path is filtered out', async () => {
    mockGitCommands((args) => {
      if (args[0] === 'status') return { stdout: '' }
      throw new Error(`Unexpected git command: ${args.join(' ')}`)
    })
    const handlers = registerHandlersForTest()
    const commit = handlers.get('git:commit')!

    await expect(
      commit({}, '/workspace', ['missing.txt', 'ignored/generated.log'], 'fix: nothing')
    ).rejects.toThrow('No Git changes to commit')

    const gitCalls = execFileMock.mock.calls.map(([, args]) => args as string[])
    expect(gitCalls).toEqual([
      ['status', '--porcelain=v1', '-z', '--untracked-files=all', '--', 'missing.txt', 'ignored/generated.log']
    ])
  })

  it('git:commit rejects paths outside the active workspace', async () => {
    const handlers = registerHandlersForTest()
    const commit = handlers.get('git:commit')!

    await expect(
      commit({}, '/workspace', ['/outside/secret.txt'], 'fix: outside')
    ).rejects.toThrow('Access denied')

    expect(execFileMock).not.toHaveBeenCalled()
  })

  it('git:commit rejects requests for a different workspace path', async () => {
    const handlers = registerHandlersForTest()
    const commit = handlers.get('git:commit')!

    await expect(
      commit({}, '/other-workspace', ['src/valid.ts'], 'fix: mismatch')
    ).rejects.toThrow('Workspace path mismatch')

    expect(execFileMock).not.toHaveBeenCalled()
  })

  it('git:commit constrains the commit pathspec to requested commit files', async () => {
    mockGitCommands((args) => {
      if (args[0] === 'status') {
        return { stdout: ' M src/valid.ts\0' }
      }
      if (args[0] === 'diff') {
        return { error: gitError(1) }
      }
      if (args[0] === 'commit') {
        return { stdout: '[main def456] fix: requested\n' }
      }
      return { stdout: '' }
    })
    const handlers = registerHandlersForTest()
    const commit = handlers.get('git:commit')!

    await commit({}, '/workspace', ['src/valid.ts'], 'fix: requested')

    const commitCall = execFileMock.mock.calls.find(([, args]) => (args as string[])[0] === 'commit')
    expect(commitCall?.[1]).toEqual(['commit', '-m', 'fix: requested', '--', 'src/valid.ts'])
    expect(commitCall?.[1]).not.toContain('src/already-staged.ts')
  })

  it('git:branch reads linked worktree branch through git commands', async () => {
    mockGitCommands((args) => {
      if (args[0] === 'rev-parse' && args[1] === '--is-inside-work-tree') return { stdout: 'true\n' }
      if (args[0] === 'branch' && args[1] === '--show-current') return { stdout: 'feat/worktree\n' }
      throw new Error(`Unexpected git command: ${args.join(' ')}`)
    })
    const handlers = registerHandlersForTest()
    const getBranch = handlers.get('git:branch')!

    const branch = await getBranch({}, '/workspace/.craft/worktrees/feat-worktree')

    expect(branch).toBe('feat/worktree')
    expect(fs.readFile).not.toHaveBeenCalled()
  })

  it('git:listBranches returns current branch and local branch list', async () => {
    mockGitCommands((args) => {
      if (args[0] === 'rev-parse' && args[1] === '--is-inside-work-tree') return { stdout: 'true\n' }
      if (args[0] === 'branch' && args[1] === '--show-current') return { stdout: 'feat/worktree\n' }
      if (args[0] === 'for-each-ref') return { stdout: 'main\nfeat/worktree\n' }
      throw new Error(`Unexpected git command: ${args.join(' ')}`)
    })
    const handlers = registerHandlersForTest()
    const listBranches = handlers.get('git:listBranches')!

    await expect(listBranches({}, '/workspace')).resolves.toEqual({
      current: 'feat/worktree',
      detachedHead: null,
      branches: [
        { name: 'main', current: false },
        { name: 'feat/worktree', current: true }
      ]
    })
  })

  it('git:checkoutBranch switches the requested branch in the provided git workspace', async () => {
    mockGitCommands((args) => {
      if (args[0] === 'switch') return { stdout: '' }
      throw new Error(`Unexpected git command: ${args.join(' ')}`)
    })
    const handlers = registerHandlersForTest()
    const checkout = handlers.get('git:checkoutBranch')!

    await checkout({}, '/workspace/.craft/worktrees/feat-worktree', 'main')

    expect(execFileMock).toHaveBeenCalledWith(
      'git',
      ['switch', 'main'],
      expect.objectContaining({ cwd: path.resolve('/workspace/.craft/worktrees/feat-worktree') }),
      expect.any(Function)
    )
  })

  it('git:createAndCheckoutBranch validates then creates the requested branch', async () => {
    mockGitCommands((args) => {
      if (args[0] === 'check-ref-format') return { stdout: 'dotcraft/new-branch\n' }
      if (args[0] === 'switch') return { stdout: '' }
      throw new Error(`Unexpected git command: ${args.join(' ')}`)
    })
    const handlers = registerHandlersForTest()
    const createAndCheckout = handlers.get('git:createAndCheckoutBranch')!

    await createAndCheckout({}, '/workspace', 'dotcraft/new-branch')

    const gitCalls = execFileMock.mock.calls.map(([, args]) => args as string[])
    expect(gitCalls).toEqual([
      ['check-ref-format', '--branch', 'dotcraft/new-branch'],
      ['switch', '-c', 'dotcraft/new-branch']
    ])
  })

  it('git:checkoutBranch rejects paths outside the workspace and managed worktrees', async () => {
    const handlers = registerHandlersForTest()
    const checkout = handlers.get('git:checkoutBranch')!

    await expect(checkout({}, '/outside/worktree', 'main')).rejects.toThrow('Workspace path mismatch')
    expect(execFileMock).not.toHaveBeenCalled()
  })

  it('workspace:search-files returns empty when no workspace is active', async () => {
    const handlers = registerHandlersForTest('')
    const searchFiles = handlers.get('workspace:search-files')!

    const result = await searchFiles({}, { query: 'src', workspacePath: '', limit: 10 })

    expect(result).toEqual({
      files: [],
      indexStatus: 'empty',
      indexedCount: 0,
      stale: false
    })
    expect(listWorkspaceFilesMock).not.toHaveBeenCalled()
  })

  it('chrome:check-setup runs Chrome setup checks and backend discovery', async () => {
    execFileMock.mockImplementation((_command, args, _options, callback: ExecFileCallback) => {
      const script = String((args as string[])[0])
      if (script.endsWith('check-extension-installed.js')) callback(null, '{"ok":true,"extensionId":"abc"}', '')
      else if (script.endsWith('check-native-host-manifest.js')) callback(null, '{"ok":true,"exists":true,"hostExists":true,"wrapperValid":true}', '')
      else if (script.endsWith('chrome-is-running.js')) callback(null, '{"ok":true,"processCount":1}', '')
      else if (script.endsWith('installed-browsers.js')) callback(null, '{"ok":true,"browsers":[]}', '')
      else callback(new Error(`Unexpected script: ${script}`), '', '')
      return null
    })
    const handlers = registerHandlersForTest()
    const checkSetup = handlers.get('chrome:check-setup')!

    const result = await checkSetup({})

    expect(result).toEqual({
      extension: { ok: true, code: 'extensionReady', message: 'DotCraft Chrome extension is ready.' },
      nativeHost: {
        ok: true,
        code: 'nativeHostReady',
        message: 'Chrome Native Host is installed.',
        safeDetails: { exists: true, hostExists: true, wrapperValid: true }
      },
      chromeRunning: { ok: true, code: 'chromeRunning', message: 'Chrome is running.', safeDetails: { processCount: 1 } },
      installedBrowsers: { ok: true, code: 'chromeInstalled', message: 'Google Chrome is installed.', safeDetails: { browserCount: 0 } },
      backend: {
        ok: false,
        code: 'backendDisconnected',
        message: 'Chrome backend is disconnected.',
        action: 'clickExtensionRefresh',
        safeDetails: { candidateCount: 0 }
      },
      bridge: {
        ok: false,
        code: 'backendDisconnected',
        message: 'Chrome backend is disconnected.',
        action: 'clickExtensionRefresh',
        safeDetails: { candidateCount: 0 }
      }
    })
    const scripts = execFileMock.mock.calls.map(([, args]) => String((args as string[])[0]).split(/[\\/]/).at(-1))
    expect(scripts).toEqual([
      'check-extension-installed.js',
      'check-native-host-manifest.js',
      'chrome-is-running.js',
      'installed-browsers.js'
    ])
    const [command, , options] = execFileMock.mock.calls[0]!
    expect(command).toBe(process.execPath)
    expect((options as { env?: NodeJS.ProcessEnv }).env?.ELECTRON_RUN_AS_NODE).toBe('1')
  })

  it('chrome:check-setup marks an old native host wrapper as repairable without leaking paths', async () => {
    execFileMock.mockImplementation((_command, args, _options, callback: ExecFileCallback) => {
      const script = String((args as string[])[0])
      if (script.endsWith('check-extension-installed.js')) callback(null, '{"ok":true}', '')
      else if (script.endsWith('check-native-host-manifest.js')) {
        callback(null, '{"ok":false,"code":"nativeHostNeedsRepair","exists":true,"hostExists":true,"wrapperValid":false,"manifestPath":"C:\\\\secret\\\\host.json","hostPath":"C:\\\\secret\\\\host.cmd"}', '')
      } else if (script.endsWith('chrome-is-running.js')) callback(null, '{"ok":true}', '')
      else if (script.endsWith('installed-browsers.js')) callback(null, '{"ok":true,"browsers":[]}', '')
      else callback(new Error(`Unexpected script: ${script}`), '', '')
      return null
    })
    const handlers = registerHandlersForTest()
    const checkSetup = handlers.get('chrome:check-setup')!

    const result = await checkSetup({}) as { nativeHost: Record<string, unknown> }

    expect(result.nativeHost).toEqual({
      ok: false,
      code: 'nativeHostNeedsRepair',
      message: 'Chrome Native Host needs to be installed or repaired.',
      action: 'repairNativeHost',
      safeDetails: { exists: true, hostExists: true, wrapperValid: false }
    })
    expect(JSON.stringify(result.nativeHost)).not.toContain('C:\\secret')
  })

  it('chrome:install-native-host runs only the bundled installer script', async () => {
    execFileMock.mockImplementation((_command, args, _options, callback: ExecFileCallback) => {
      callback(null, '{"ok":true,"manifestPath":"host.json"}', '')
      return null
    })
    const handlers = registerHandlersForTest()
    const installNativeHost = handlers.get('chrome:install-native-host')!

    const result = await installNativeHost({})

    expect(result).toEqual({ ok: true, manifestPath: 'host.json' })
    expect(execFileMock).toHaveBeenCalledOnce()
    const args = execFileMock.mock.calls[0]![1] as string[]
    expect(args[0]).toMatch(/installManifest\.mjs$/)
    expect(args.slice(1)).toEqual([])
  })

  it('chrome:open only forwards normalized Chrome launch targets', async () => {
    execFileMock.mockImplementation((_command, _args, _options, callback: ExecFileCallback) => {
      callback(null, '{"ok":true,"opened":true}', '')
      return null
    })
    const handlers = registerHandlersForTest()
    const openChrome = handlers.get('chrome:open')!

    await openChrome({}, { url: 'chrome://extensions/?id=abc' })
    await openChrome({}, { url: 'file:///C:/secret.txt' })

    expect(execFileMock).toHaveBeenCalledTimes(2)
    const firstArgs = execFileMock.mock.calls[0]![1] as string[]
    const secondArgs = execFileMock.mock.calls[1]![1] as string[]
    expect(firstArgs.at(-1)).toBe('chrome://extensions/?id=abc')
    expect(secondArgs.at(-1)).toBe('about:blank')
  })

  it('registers editors:list and returns detected editor entries', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false, providers: [] }))
    })

    const result = await handlers.get('editors:list')?.({})
    expect(detectEditorsMock).toHaveBeenCalledOnce()
    expect(result).toEqual([
      { id: 'cursor', labelKey: 'editors.cursor', iconKey: 'editor-generic' },
      { id: 'explorer', labelKey: 'editors.explorer', iconKey: 'explorer' }
    ])
  })

  it('registers editors:launch and validates workspace path before launch', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      getSettings: vi.fn(() => ({ locale: 'en' })),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false, providers: [] }))
    })

    await handlers.get('editors:launch')?.({}, 'cursor', '/workspace')
    expect(launchEditorMock).toHaveBeenCalledWith('cursor', expect.stringMatching(/^[A-Z]:\\workspace$/))

    await expect(
      handlers.get('editors:launch')?.({}, 'cursor', '/outside')
    ).rejects.toThrow()
  })

  it('rejects unsafe local path open requests before invoking shell or editor actions', async () => {
    const handlers = registerHandlersForTest()
    const openLocalPath = handlers.get('shell:open-local-path')!

    await expect(openLocalPath({}, 'relative/path.txt')).rejects.toThrow(
      'Local path must be absolute'
    )
    await expect(openLocalPath({}, 'file:///tmp/path.txt')).rejects.toThrow(
      'Local path must be an absolute filesystem path'
    )

    vi.mocked(fs.stat).mockRejectedValueOnce(Object.assign(new Error('missing'), { code: 'ENOENT' }))
    await expect(openLocalPath({}, path.resolve('/tmp/missing.txt'))).rejects.toThrow('missing')

    expect(shell.openPath).not.toHaveBeenCalled()
    expect(shell.showItemInFolder).not.toHaveBeenCalled()
    expect(launchEditorMock).not.toHaveBeenCalled()
  })

  it('opens existing local paths with editor, default app, and Explorer handlers', async () => {
    const handlers = registerHandlersForTest()
    const targetPath = path.resolve('/tmp/dotcraft-local-note.txt')
    vi.mocked(fs.stat).mockResolvedValue({} as Awaited<ReturnType<typeof fs.stat>>)

    await handlers.get('editors:launch-local-path')?.({}, 'cursor', targetPath)
    await handlers.get('shell:open-local-path')?.({}, targetPath)
    await handlers.get('shell:reveal-local-path')?.({}, targetPath)

    expect(launchEditorMock).toHaveBeenCalledWith('cursor', targetPath)
    expect(shell.openPath).toHaveBeenCalledWith(targetPath)
    expect(shell.showItemInFolder).toHaveBeenCalledWith(targetPath)
  })

  it('registers appserver:restart-managed and forwards to callback', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })

    const onRestartManagedAppServer = vi.fn().mockResolvedValue(undefined)
    const onListSetupModels = vi.fn().mockResolvedValue({ kind: 'unsupported' })

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels,
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer,
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false, providers: [] }))
    })

    expect(handlers.has('appserver:restart-managed')).toBe(true)
    await handlers.get('appserver:restart-managed')?.({})
    expect(onRestartManagedAppServer).toHaveBeenCalledOnce()
  })

  it('registers appserver:apply-connection-settings and forwards the draft to callback', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    const onApplyConnectionSettings = vi.fn().mockResolvedValue(undefined)
    const draft = {
      connectionMode: 'remote' as const,
      remote: { url: 'ws://127.0.0.1:9100/ws', token: 'fixture-remote-token' }
    }

    registerIpcHandlers(null, () => null, '/workspace', createIpcCallbacks({
      onApplyConnectionSettings
    }))

    expect(handlers.has('appserver:apply-connection-settings')).toBe(true)
    await handlers.get('appserver:apply-connection-settings')?.({}, draft)
    expect(onApplyConnectionSettings).toHaveBeenCalledWith(draft)
  })

  it('remote stack Open in Desktop activates a saved stack without persisting a localhost remote URL', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    const updateSettings = vi.fn()
    const onConnectRemoteStack = vi.fn().mockResolvedValue({ localPort: 51523 })
    const host = {
      id: 'h1',
      name: 'Cloud',
      sshTarget: 'user@cloud',
      stacks: [{
        id: 's1',
        name: 'demo-stack',
        composeDir: '/srv/sample/demo-stack/deploy',
        workspaceDir: '/srv/sample/demo-stack/deploy/workspace',
        appServerPort: 9100,
        dashboardPort: 8080,
        sandboxProfile: false
      }]
    }

    registerIpcHandlers(null, () => null, '/workspace', createIpcCallbacks({
      getSettings: vi.fn(() => ({ remoteHosts: [host] })),
      updateSettings,
      onConnectRemoteStack
    }))

    const result = await handlers.get('remoteStacks:open-app-server-tunnel')?.({}, {
      hostId: 'h1',
      stackId: 's1'
    })

    expect(onConnectRemoteStack).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'h1' }),
      expect.objectContaining({ id: 's1' })
    )
    expect(updateSettings).not.toHaveBeenCalledWith(
      expect.objectContaining({
        connectionMode: 'remote',
        remote: expect.objectContaining({ url: expect.stringContaining('127.0.0.1') })
      })
    )
    expect(result).toEqual({ ok: true, hostId: 'h1', stackId: 's1', localPort: 51523 })
  })

  it('remote stack disconnect forwards to the active-stack disconnect callback', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    const onDisconnectRemoteStack = vi.fn().mockResolvedValue(undefined)

    registerIpcHandlers(null, () => null, '/workspace', createIpcCallbacks({
      onDisconnectRemoteStack
    }))

    await handlers.get('remoteStacks:disconnect')?.({}, { hostId: 'h1', stackId: 's1' })
    expect(onDisconnectRemoteStack).toHaveBeenCalledWith('h1', 's1')
  })

  it('workspace-config:get-core reads nested Skills.SelfLearning.Enabled values', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    vi.mocked(fs.readFile).mockImplementation(async (filePath) => {
      const pathText = String(filePath)
      if (pathText.includes('sample-project')) {
        return JSON.stringify({
          Memory: {
            AutoConsolidateEnabled: true
          },
          Skills: {
            SelfLearning: {
              Enabled: true
            }
          }
        })
      }
      return JSON.stringify({
        Memory: {
          AutoConsolidateEnabled: false
        },
        Skills: {
          SelfLearning: {
            Enabled: false
          }
        }
      })
    })

    registerIpcHandlers(null, () => null, path.join('/workspace', 'sample-project'), {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({
        status: 'ready',
        workspacePath: 'C:\\sample\\workspace',
        hasUserConfig: true,
        providers: []
      }))
    })

    const result = await handlers.get('workspace-config:get-core')?.({})
    expect(result).toMatchObject({
      workspace: { skillsSelfLearningEnabled: true, memoryAutoConsolidateEnabled: true },
      userDefaults: { skillsSelfLearningEnabled: false, memoryAutoConsolidateEnabled: false }
    })
  })

  it('workspace-config:get-core reads the active remote stack config over SSH instead of local files', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    const host = {
      id: 'h1',
      name: 'Remote Lab',
      sshTarget: 'remote-test-host',
      stacks: [{
        id: 's1',
        name: 'ChatOps',
        composeDir: '/srv/dotcraft/chatops/deploy',
        workspaceDir: '/srv/dotcraft/chatops/deploy/workspace',
        appServerPort: 9100,
        dashboardPort: 8080,
        sandboxProfile: false
      }]
    }
    const readCoreConfig = vi.spyOn(getRemoteServersManager(), 'readCoreConfig').mockResolvedValue({
      workspaceRaw: JSON.stringify({
        ProviderId: 'anthropic-main',
        Model: 'claude-sonnet-4-5',
        Permissions: { DefaultApprovalPolicy: 'autoApprove' }
      }),
      userDefaultsRaw: JSON.stringify({
        ProviderId: 'openai',
        Model: 'gpt-5'
      })
    })

    try {
      registerIpcHandlers(null, () => null, '/local/workspace', createIpcCallbacks({
        getSettings: vi.fn(() => ({
          locale: 'en',
          connectionMode: 'remote',
          activeRemoteStack: { hostId: 'h1', stackId: 's1' },
          remoteHosts: [host]
        })),
        getWorkspaceStatus: vi.fn(() => ({
          status: 'ready',
          workspacePath: '/local/workspace',
          hasUserConfig: true,
          providers: [],
          remote: {
            hostId: 'h1',
            stackId: 's1',
            serverName: 'Remote Lab',
            stackName: 'ChatOps',
            workspaceDir: '/srv/dotcraft/chatops/deploy/workspace',
            appServerWorkspacePath: '/workspace',
            composeDir: '/srv/dotcraft/chatops/deploy',
            projectName: 'deploy'
          }
        }))
      }))

      const result = await handlers.get('workspace-config:get-core')?.({})

      expect(readCoreConfig).toHaveBeenCalledWith(
        expect.objectContaining({ id: 'h1' }),
        expect.objectContaining({ id: 's1' })
      )
      expect(fs.readFile).not.toHaveBeenCalled()
      expect(result).toMatchObject({
        workspace: {
          providerId: 'anthropic-main',
          model: 'claude-sonnet-4-5',
          defaultApprovalPolicy: 'autoApprove'
        },
        userDefaults: {
          providerId: 'openai',
          model: 'gpt-5'
        }
      })
    } finally {
      readCoreConfig.mockRestore()
    }
  })

  it('registers workspace:list-setup-models and forwards to callback', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })

    const onListSetupModels = vi.fn().mockResolvedValue({ kind: 'success', models: ['gpt-4.1'] })

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels,
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false, providers: [] }))
    })

    expect(handlers.has('workspace:list-setup-models')).toBe(true)
    const result = await handlers.get('workspace:list-setup-models')?.({}, {
      providerId: 'anthropic'
    })
    expect(onListSetupModels).toHaveBeenCalledOnce()
    expect(result).toEqual({ kind: 'success', models: ['gpt-4.1'] })
  })

  it('rethrows invalid JSON from modules:read-config instead of returning an empty object', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    vi.mocked(fs.stat).mockResolvedValue({ size: 32 } as Awaited<ReturnType<typeof fs.stat>>)
    vi.mocked(fs.readFile).mockResolvedValue('{invalid-json' as Awaited<ReturnType<typeof fs.readFile>>)

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false, providers: [] }))
    })

    await expect(
      handlers.get('modules:read-config')?.({}, { configFileName: 'module.json' })
    ).rejects.toThrow()
  })

  it('reads BOM-prefixed JSON in modules:read-config', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    vi.mocked(fs.stat).mockResolvedValue({ size: 32 } as Awaited<ReturnType<typeof fs.stat>>)
    vi.mocked(fs.readFile).mockResolvedValue('\uFEFF{"Enabled":true}' as Awaited<ReturnType<typeof fs.readFile>>)

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false, providers: [] }))
    })

    await expect(
      handlers.get('modules:read-config')?.({}, { configFileName: 'module.json' })
    ).resolves.toEqual({
      exists: true,
      config: { Enabled: true }
    })
  })

  it('returns an error for invalid JSON in modules:start and does not overwrite the config file', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    scanModulesMock.mockResolvedValue([
      {
        moduleId: 'demo-module',
        channelName: 'demo',
        displayName: 'Demo',
        packageName: 'demo-module',
        configFileName: 'module.json',
        supportedTransports: ['stdio'],
        requiresInteractiveSetup: false,
        variant: 'default',
        source: 'user',
        absolutePath: '/workspace/modules/demo',
        configDescriptors: []
      }
    ])
    vi.mocked(fs.readFile).mockResolvedValue('{invalid-json' as Awaited<ReturnType<typeof fs.readFile>>)

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false, providers: [] }))
    })

    await expect(
      handlers.get('modules:start')?.({}, { moduleId: 'demo-module' })
    ).resolves.toMatchObject({ ok: false })
    expect(vi.mocked(fs.writeFile)).not.toHaveBeenCalled()
    expect(moduleProcessManagerStartMock).not.toHaveBeenCalled()
  })

  it('returns an object-type error for non-object JSON in modules:start and does not overwrite the config file', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    scanModulesMock.mockResolvedValue([
      {
        moduleId: 'demo-module',
        channelName: 'demo',
        displayName: 'Demo',
        packageName: 'demo-module',
        configFileName: 'module.json',
        supportedTransports: ['stdio'],
        requiresInteractiveSetup: false,
        variant: 'default',
        source: 'user',
        absolutePath: '/workspace/modules/demo',
        configDescriptors: []
      }
    ])
    vi.mocked(fs.readFile).mockResolvedValue('["not-an-object"]' as Awaited<ReturnType<typeof fs.readFile>>)

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false, providers: [] }))
    })

    await expect(
      handlers.get('modules:start')?.({}, { moduleId: 'demo-module' })
    ).resolves.toEqual({ ok: false, error: 'Config payload must be a JSON object' })
    expect(vi.mocked(fs.writeFile)).not.toHaveBeenCalled()
    expect(moduleProcessManagerStartMock).not.toHaveBeenCalled()
  })

  it('awaits async updateSettings in settings:set handler', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })

    let resolveUpdate: (() => void) | null = null
    const updateSettings = vi.fn(
      () =>
        new Promise<void>((resolve) => {
          resolveUpdate = resolve
        })
    )

    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      getSettings: vi.fn(() => ({})),
      updateSettings,
      getRecentWorkspaces: vi.fn(() => []),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false, providers: [] }))
    })

    const settingsSet = handlers.get('settings:set')
    expect(settingsSet).toBeDefined()

    let settled = false
    const pending = Promise.resolve(settingsSet?.({}, { notifications: { taskCompletionMode: 'always' } })).then(() => {
      settled = true
    })

    await Promise.resolve()
    expect(updateSettings).toHaveBeenCalledOnce()
    expect(settled).toBe(false)

    resolveUpdate?.()
    await pending
    expect(settled).toBe(true)
  })
})

describe('task completion notifications', () => {
  function createWindow(focused: boolean): Electron.BrowserWindow {
    return {
      isDestroyed: vi.fn(() => false),
      isFocused: vi.fn(() => focused),
      webContents: {
        send: vi.fn()
      }
    } as unknown as Electron.BrowserWindow
  }

  beforeEach(() => {
    vi.clearAllMocks()
    vi.mocked(Notification.isSupported).mockReturnValue(true)
  })

  it('defaults to showing task completion notifications only when unfocused', () => {
    expect(shouldShowTaskCompletionNotification(createWindow(false))).toBe(true)
    expect(shouldShowTaskCompletionNotification(createWindow(true))).toBe(false)
  })

  it('honors always and never task completion notification settings', () => {
    expect(shouldShowTaskCompletionNotification(createWindow(true), {
      notifications: { taskCompletionMode: 'always' }
    })).toBe(true)
    expect(shouldShowTaskCompletionNotification(createWindow(false), {
      notifications: { taskCompletionMode: 'never' }
    })).toBe(false)
  })

  it('shows native job result notifications according to settings while still forwarding renderer events', () => {
    const win = createWindow(true)

    broadcastNotification(win, 'system/jobResult', {
      jobName: 'Heartbeat',
      result: '**Done** with `task`'
    }, {
      notifications: { taskCompletionMode: 'always' }
    })

    expect(Notification).toHaveBeenCalledWith({
      title: 'Heartbeat',
      body: 'Done with task'
    })
    expect(notificationShowMock).toHaveBeenCalledOnce()
    expect(win.webContents.send).toHaveBeenCalledWith('appserver:notification', {
      method: 'system/jobResult',
      params: {
        jobName: 'Heartbeat',
        result: '**Done** with `task`'
      }
    })
  })

  it('suppresses native job result notifications when disabled but still forwards renderer events', () => {
    const win = createWindow(false)

    broadcastNotification(win, 'system/jobResult', {
      jobName: 'Cron',
      result: 'Done'
    }, {
      notifications: { taskCompletionMode: 'never' }
    })

    expect(Notification).not.toHaveBeenCalled()
    expect(notificationShowMock).not.toHaveBeenCalled()
    expect(win.webContents.send).toHaveBeenCalledWith('appserver:notification', {
      method: 'system/jobResult',
      params: {
        jobName: 'Cron',
        result: 'Done'
      }
    })
  })

  it('tags forwarded renderer notifications with a workspace path when provided', () => {
    const win = createWindow(false)

    broadcastNotification(win, 'thread/runtimeChanged', {
      threadId: 'thread_1',
      runtime: { running: true }
    }, undefined, 'F:/workspace-b')

    expect(win.webContents.send).toHaveBeenCalledWith('appserver:notification', {
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread_1',
        runtime: { running: true }
      },
      workspacePath: 'F:/workspace-b'
    })
  })

  it('tags forwarded renderer notifications with foreground state when provided', () => {
    const win = createWindow(false)

    broadcastNotification(win, 'thread/runtimeChanged', {
      threadId: 'thread_1',
      runtime: { running: true }
    }, undefined, 'F:/workspace-b', false)

    expect(win.webContents.send).toHaveBeenCalledWith('appserver:notification', {
      method: 'thread/runtimeChanged',
      params: {
        threadId: 'thread_1',
        runtime: { running: true }
      },
      workspacePath: 'F:/workspace-b',
      foreground: false
    })
  })

  it('shows native user input request notifications when unfocused', () => {
    const win = createWindow(false)
    const payload = {
      bridgeId: 'bridge-1',
      method: 'item/tool/requestUserInput',
      params: {
        questions: [
          { question: 'Which option should DotCraft use?' }
        ]
      }
    }

    broadcastServerRequest(win, payload, { locale: 'en' })

    expect(Notification).toHaveBeenCalledWith({
      title: 'DotCraft needs your answer',
      body: 'Which option should DotCraft use?'
    })
    expect(notificationShowMock).toHaveBeenCalledOnce()
    expect(win.webContents.send).toHaveBeenCalledWith('appserver:server-request', payload)
  })

  it('shows localized approval request notifications when unfocused', () => {
    const win = createWindow(false)
    const payload = {
      bridgeId: 'bridge-2',
      method: 'item/approval/request',
      params: {
        reason: '需要运行命令'
      }
    }

    broadcastServerRequest(win, payload, { locale: 'zh-Hans' })

    expect(Notification).toHaveBeenCalledWith({
      title: 'DotCraft 需要你审批',
      body: '需要运行命令'
    })
    expect(notificationShowMock).toHaveBeenCalledOnce()
    expect(win.webContents.send).toHaveBeenCalledWith('appserver:server-request', payload)
  })

  it('does not show interactive request notifications while focused', () => {
    const win = createWindow(true)
    const payload = {
      bridgeId: 'bridge-3',
      method: 'item/tool/requestUserInput',
      params: {
        questions: [
          { question: 'Choose one.' }
        ]
      }
    }

    broadcastServerRequest(win, payload, { locale: 'en' })

    expect(Notification).not.toHaveBeenCalled()
    expect(notificationShowMock).not.toHaveBeenCalled()
    expect(win.webContents.send).toHaveBeenCalledWith('appserver:server-request', payload)
  })
})

describe('unregisterIpcHandlers', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('removes workspace-config:get-core and workspace recent-project handlers during teardown', () => {
    unregisterIpcHandlers()

    const removedChannels = vi.mocked(ipcMain.removeHandler).mock.calls.map(([channel]) => channel)
    expect(removedChannels).toContain('workspace-config:get-core')
    expect(removedChannels).toContain('workspace:get-projects')
    expect(removedChannels).toContain('workspace:remove-recent')
    expect(removedChannels).toContain('workspace:disconnect-remote')
    expect(removedChannels).toContain('workspace:clear-recent')
    expect(removedChannels.filter((channel) => channel === 'workspace-config:get-core')).toHaveLength(1)
    expect(removedChannels.filter((channel) => channel === 'workspace:get-projects')).toHaveLength(1)
    expect(removedChannels.filter((channel) => channel === 'workspace:remove-recent')).toHaveLength(1)
    expect(removedChannels.filter((channel) => channel === 'workspace:disconnect-remote')).toHaveLength(1)
    expect(removedChannels.filter((channel) => channel === 'workspace:clear-recent')).toHaveLength(1)
  })

  it('does not close remote tunnels while re-registering IPC handlers', () => {
    const closeAllTunnels = vi.spyOn(getRemoteServersManager(), 'closeAllTunnels')

    try {
      unregisterIpcHandlers()

      expect(closeAllTunnels).not.toHaveBeenCalled()
    } finally {
      closeAllTunnels.mockRestore()
    }
  })

  it('removes the new workspace handlers after they are registered', () => {
    registerIpcHandlers(null, () => null, '/workspace', {
      onSwitchWorkspace: vi.fn().mockResolvedValue(undefined),
      onClearWorkspaceSelection: vi.fn().mockResolvedValue(undefined),
      onRunWorkspaceSetup: vi.fn().mockResolvedValue(undefined),
      onListSetupModels: vi.fn().mockResolvedValue({ kind: 'unsupported' }),
      onOpenNewWindow: vi.fn(),
      onRestartManagedAppServer: vi.fn().mockResolvedValue(undefined),
      getSettings: vi.fn(() => ({})),
      updateSettings: vi.fn(),
      getRecentWorkspaces: vi.fn(() => []),
      clearRecentWorkspaces: vi.fn(),
      getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
      getWorkspaceStatus: vi.fn(() => ({ status: 'no-workspace', workspacePath: '', hasUserConfig: false, providers: [] }))
    })

    vi.mocked(ipcMain.removeHandler).mockClear()

    unregisterIpcHandlers()

    expect(ipcMain.removeHandler).toHaveBeenCalledWith('workspace-config:get-core')
    expect(ipcMain.removeHandler).toHaveBeenCalledWith('workspace:get-projects')
    expect(ipcMain.removeHandler).toHaveBeenCalledWith('workspace:remove-recent')
    expect(ipcMain.removeHandler).toHaveBeenCalledWith('workspace:disconnect-remote')
    expect(ipcMain.removeHandler).toHaveBeenCalledWith('workspace:clear-recent')
  })
})

// ---------------------------------------------------------------------------
// WireProtocolClient — bidirectional request routing
// (covered in WireProtocolClient.test.ts, but verified here as integration)
// ---------------------------------------------------------------------------

import { Readable, Writable, PassThrough } from 'stream'
import { WireProtocolClient } from '../WireProtocolClient'

describe('WireProtocolClient bidirectional routing', () => {
  it('server request handler result is sent back as JSON-RPC response with original id', async () => {
    const toServer = new PassThrough()
    const fromServer = new PassThrough()
    const client = new WireProtocolClient(
      fromServer as unknown as Readable,
      toServer as unknown as Writable
    )

    const responseLines: string[] = []
    toServer.on('data', (chunk: Buffer) => {
      chunk.toString('utf8').split('\n').filter(Boolean).forEach((l) => responseLines.push(l))
    })

    // Register a handler that simulates the approval bridge: returns the decision
    client.onServerRequest(async (_method, params) => {
      const p = params as Record<string, unknown>
      return { decision: p.defaultDecision ?? 'accept' }
    })

    // AppServer sends a server-initiated request
    fromServer.push(
      JSON.stringify({
        jsonrpc: '2.0',
        id: 42,
        method: 'item/approval/request',
        params: { approvalType: 'shell', operation: 'rm -rf /tmp', defaultDecision: 'decline' }
      }) + '\n'
    )

    await new Promise((r) => setTimeout(r, 20))

    // Filter out any initialize or other requests from the response lines
    const approvalResponse = responseLines
      .map((l) => JSON.parse(l))
      .find((m) => m.id === 42 && 'result' in m)

    expect(approvalResponse).toBeDefined()
    expect(approvalResponse).toMatchObject({
      jsonrpc: '2.0',
      id: 42,
      result: { decision: 'decline' }
    })

    client.dispose()
    toServer.destroy()
    fromServer.destroy()
  })

  it('sends an error response when handler throws', async () => {
    const toServer = new PassThrough()
    const fromServer = new PassThrough()
    const client = new WireProtocolClient(
      fromServer as unknown as Readable,
      toServer as unknown as Writable
    )

    const responseLines: string[] = []
    toServer.on('data', (chunk: Buffer) => {
      chunk.toString('utf8').split('\n').filter(Boolean).forEach((l) => responseLines.push(l))
    })

    client.onServerRequest(async () => {
      throw new Error('Bridge unavailable')
    })

    fromServer.push(
      JSON.stringify({ jsonrpc: '2.0', id: 77, method: 'item/approval/request', params: {} }) + '\n'
    )

    await new Promise((r) => setTimeout(r, 20))

    const errorResponse = responseLines
      .map((l) => JSON.parse(l))
      .find((m) => m.id === 77 && 'error' in m)

    expect(errorResponse).toBeDefined()
    expect(errorResponse.error.code).toBe(-32603)

    client.dispose()
    toServer.destroy()
    fromServer.destroy()
  })
})

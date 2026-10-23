import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { ipcMain, Notification, shell } from 'electron'
import { promises as fs } from 'fs'
import * as path from 'path'
import { PassThrough, type Readable, type Writable } from 'stream'

const {
  scanModulesMock,
  channelModuleManagerStartMock,
  openInTargetMock,
  execFileMock,
  existsSyncMock,
  readFileSyncMock,
  listWorkspaceFilesMock,
  notificationShowMock
} = vi.hoisted(() => ({
  scanModulesMock: vi.fn(),
  channelModuleManagerStartMock: vi.fn(),
  openInTargetMock: vi.fn(),
  execFileMock: vi.fn(),
  existsSyncMock: vi.fn(),
  readFileSyncMock: vi.fn(),
  listWorkspaceFilesMock: vi.fn(),
  notificationShowMock: vi.fn()
}))

vi.mock('fs', () => ({
  existsSync: existsSyncMock,
  readFileSync: readFileSyncMock,
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
    getApplicationNameForProtocol: vi.fn((url: string) => url.startsWith('workflow://') ? 'Workflow App' : '')
  },
  ipcMain: {
    handle: vi.fn(),
    removeHandler: vi.fn(),
    on: vi.fn(),
    removeAllListeners: vi.fn()
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

vi.mock('../channelModuleManager', async () => {
  const actual = await vi.importActual('../channelModuleManager')
  class MockChannelModuleManager {
    start = channelModuleManagerStartMock
    stop = vi.fn()
    getStatusMap = vi.fn(() => ({}))
    restoreModules = vi.fn().mockResolvedValue(undefined)
    dispose = vi.fn()
    getRecentLogs = vi.fn(() => [])
    getQrStatus = vi.fn(() => ({ active: false, qrDataUrl: null }))
  }
  return {
    ...actual,
    ChannelModuleManager: MockChannelModuleManager
  }
})

vi.mock('../openTargets', () => ({
  listOpenTargets: vi.fn().mockResolvedValue([]),
  openInTarget: openInTargetMock
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
  cancelServerRequestBridge,
  createServerRequestBridge,
  resolveServerRequestBridge,
  registerIpcHandlers,
  unregisterIpcHandlers,
  sanitizeHttpOrHttpsUrl,
  sanitizeExternalUrl,
  openExternalUrl,
  openExternalHttpUrl,
  openAppHandoffUrl,
  broadcastNotification,
  broadcastServerRequest,
  shouldShowTaskCompletionNotification
} from '../ipcBridge'
import { setDesktopServiceHandoffHandler } from '../desktopServiceHandoff'
import { SshMachinesManager } from '../sshMachines/sshMachinesManager'
import { normalizeSshMachines } from '../../shared/sshMachines'
import { unwrapAppServerResult } from '../../shared/appServerError'
import { applyWorkspaceThreadListRequestResult, beginWorkspaceThreadListRequest } from '../workspaceThreadCache'
import { setLocalChatsFolderRootsResolver } from '../chatFolderAccess'
import { resolveDefaultChatWorkspacePath } from '../defaultChatWorkspace'
import { isTerminalCwdAllowed } from '../viewerTerminalIpc'
import { DesktopAppServerClient } from '../DesktopAppServerClient'

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
    getLocalProjectPaths: vi.fn(() => []),
    getConnectionStatus: vi.fn(() => ({ status: 'disconnected' })),
    getWorkspaceStatus: vi.fn(() => ({ status: 'ready', workspacePath: '/workspace', hasUserConfig: false, providers: [] })),
    ...overrides
  }
}

function machinesManagerFor(hosts: unknown[]): SshMachinesManager {
  const machines = normalizeSshMachines(hosts)
  return new SshMachinesManager({
    loadMachines: () => structuredClone(machines),
    saveMachines: vi.fn(),
    installScript: () => '',
    appVersion: '0.0.0',
    packaged: false
  })
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


describe('createServerRequestBridge', () => {
  it('resolves a bridge and runs its settlement callback once', async () => {
    const onSettled = vi.fn()
    const { bridgeId, promise } = createServerRequestBridge(onSettled)

    resolveServerRequestBridge(bridgeId, { answers: {} })
    await expect(promise).resolves.toEqual({ answers: {} })
    resolveServerRequestBridge(bridgeId, null)
    expect(onSettled).toHaveBeenCalledTimes(1)
  })

  it('rejects a cancelled bridge and runs its settlement callback once', async () => {
    const onSettled = vi.fn()
    const { bridgeId, promise } = createServerRequestBridge(onSettled)

    cancelServerRequestBridge(bridgeId)
    await expect(promise).rejects.toThrow()
    cancelServerRequestBridge(bridgeId)
    expect(onSettled).toHaveBeenCalledTimes(1)
  })
})

describe('URL sanitizers', () => {
  it('sanitizeHttpOrHttpsUrl accepts only well-formed http(s) URLs', () => {
    expect(sanitizeHttpOrHttpsUrl('http://127.0.0.1:8080/dashboard')).toBe('http://127.0.0.1:8080/dashboard')
    expect(sanitizeHttpOrHttpsUrl('https://example.com/path')).toBe('https://example.com/path')
    for (const value of [undefined, '', '   ', 'file:///etc/passwd', 'ms-msdt:foo', 'custom:host', 'not a url']) {
      expect(sanitizeHttpOrHttpsUrl(value)).toBeNull()
    }
  })

  it('sanitizeExternalUrl accepts http(s), mailto, and tel URLs only', () => {
    expect(sanitizeExternalUrl('https://example.com')).toBe('https://example.com/')
    expect(sanitizeExternalUrl('mailto:test@example.com')).toBe('mailto:test@example.com')
    expect(sanitizeExternalUrl('tel:+123456789')).toBe('tel:+123456789')
    expect(sanitizeExternalUrl('file:///tmp/x')).toBeNull()
    expect(sanitizeExternalUrl(`https://example.com/${'a'.repeat(5000)}`)).toBeNull()
  })
})

describe('openExternalUrl', () => {
  it('opens allowed schemes and rejects everything else', async () => {
    vi.mocked(shell.openExternal).mockClear()
    await openExternalHttpUrl('https://example.com')
    await openExternalUrl('mailto:test@example.com')
    await openExternalUrl('workflow://dotcraft/connect?request=req_1')

    expect(vi.mocked(shell.openExternal).mock.calls.map(([url]) => url)).toEqual([
      'https://example.com/',
      'mailto:test@example.com',
      'workflow://dotcraft/connect?request=req_1'
    ])
    await expect(openExternalHttpUrl('')).rejects.toThrow('Invalid URL')
    await expect(openExternalHttpUrl('file:///tmp/x')).rejects.toThrow('Only http(s) URLs are allowed')
    await expect(openExternalUrl('file:///tmp/x')).rejects.toThrow('URL scheme is not allowed')
  })
})

describe('openAppHandoffUrl', () => {
  it('routes managed service handoffs inside Desktop Main', async () => {
    const handler = vi.fn().mockResolvedValue(undefined)
    vi.mocked(shell.openExternal).mockClear()
    setDesktopServiceHandoffHandler(handler)
    try {
      const url = 'dotcraft-service://oratorio/connect?request=req_1'
      await openAppHandoffUrl(url)
      expect(handler).toHaveBeenCalledWith(url)
      expect(shell.openExternal).not.toHaveBeenCalled()
    } finally {
      setDesktopServiceHandoffHandler(null)
    }
  })

  it('invokes loopback HTTP handoffs without opening the browser', async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: true, status: 200 })
    vi.stubGlobal('fetch', fetchMock)
    vi.mocked(shell.openExternal).mockClear()

    await openAppHandoffUrl('http://127.0.0.1:39777/dotcraft/bind?app=com.example.dynamic-tools')

    expect(fetchMock).toHaveBeenCalledTimes(1)
    expect(shell.openExternal).not.toHaveBeenCalled()
    vi.unstubAllGlobals()
  })

  it('falls back to the OS handler for custom protocol handoffs', async () => {
    vi.mocked(shell.openExternal).mockClear()

    await openAppHandoffUrl('workflow://dotcraft/bind?request=req_1')

    expect(shell.openExternal).toHaveBeenCalledWith('workflow://dotcraft/bind?request=req_1')
  })
})

describe('registerIpcHandlers', () => {
  beforeEach(async () => {
    vi.clearAllMocks()
    scanModulesMock.mockResolvedValue([])
    channelModuleManagerStartMock.mockResolvedValue({ ok: true })
    readFileSyncMock.mockReturnValue('{"extensionId":"pekajfcokkicggfjmickmkngmmoojlda"}')
    openInTargetMock.mockResolvedValue(undefined)
    listWorkspaceFilesMock.mockResolvedValue({
      files: [],
      indexStatus: 'ready',
      indexedCount: 0,
      stale: false
    })
    existsSyncMock.mockReturnValue(true)
  })

  it('keeps a connection-keyed request on its connection after another workspace comes to the foreground', async () => {
    const clientA = { sendRequest: vi.fn().mockResolvedValue({}) }
    const clientB = { sendRequest: vi.fn().mockResolvedValue({}) }
    const keys = new Map<unknown, string>([[clientA, 'project-a'], [clientB, 'project-b']])
    let foreground: unknown = clientA
    const handlers = registerHandlersForTest('/project-a', () => foreground as never, createIpcCallbacks({
      getConnectionKey: (client) => keys.get(client) ?? null,
      getConnectionClient: (key) => ([...keys].find(([, candidate]) => candidate === key)?.[0] ?? null) as never
    }))

    const key = await handlers.get('appserver:get-connection-key')?.({})
    foreground = clientB
    await handlers.get('appserver:send-connection-request')?.({}, key, 'thread/delete', { threadId: 'side-1' })

    expect(key).toBe('project-a')
    expect(clientA.sendRequest).toHaveBeenCalledWith('thread/delete', { threadId: 'side-1' }, undefined)
    expect(clientB.sendRequest).not.toHaveBeenCalled()
  })

  it('recovers each project cache through the originating client when foreground projects switch during list loads', async () => {
    let resolveA!: (result: { data: unknown[] }) => void
    const resultA = { data: [{ id: 'running-a', runtime: { state: 'running' } }] }
    const resultB = { data: [{ id: 'running-b', runtime: { state: 'running' } }] }
    const clientA = { sendRequest: vi.fn(() => new Promise<{ data: unknown[] }>((resolve) => { resolveA = resolve })) }
    const clientB = { sendRequest: vi.fn().mockResolvedValue(resultB) }
    const entryA = { workspacePath: '/project-a', threads: [], errorMessage: 'thread/list timed out' }
    const entryB = { workspacePath: '/project-b', threads: [], errorMessage: 'thread/list timed out' }
    const entries = new Map([[clientA, entryA], [clientB, entryB]])
    let foreground = clientA
    const onStarted = vi.fn((client, method, params) => {
      const entry = entries.get(client)
      return entry ? beginWorkspaceThreadListRequest(entry, method, params) : undefined
    })
    const onCompleted = vi.fn((client, method, params, result, generation) => {
      const entry = entries.get(client)
      if (entry) applyWorkspaceThreadListRequestResult(entry, method, params, result, generation)
    })
    const handlers = registerHandlersForTest('/project-a', () => foreground as never, createIpcCallbacks({
      onAppServerRequestStarted: onStarted,
      onAppServerRequestCompleted: onCompleted
    }))
    const params = (workspacePath: string) => ({ identity: { workspacePath }, scope: 'workspace', includeSubAgents: true })
    const pendingA = handlers.get('appserver:send-request')?.({}, 'thread/list', params('/project-a'))

    foreground = clientB
    await expect(handlers.get('appserver:send-request')?.({}, 'thread/list', params('/project-b'))).resolves.toEqual(resultB)
    resolveA(resultA)
    await expect(pendingA).resolves.toEqual(resultA)

    expect(onCompleted).toHaveBeenCalledWith(clientA, 'thread/list', params('/project-a'), resultA, expect.any(Number))
    expect(onCompleted).toHaveBeenCalledWith(clientB, 'thread/list', params('/project-b'), resultB, expect.any(Number))
    expect(entryA.threads).toEqual(resultA.data)
    expect(entryB.threads).toEqual(resultB.data)
    expect(entryA.errorMessage).toBeUndefined()
    expect(entryB.errorMessage).toBeUndefined()
    foreground = clientA
    expect(entries.get(foreground)?.threads).toEqual(resultA.data)
    foreground = clientB
    expect(entries.get(foreground)?.threads).toEqual(resultB.data)
  })

  it('captures foreground list generations before dispatch and rejects out-of-order IPC completions', async () => {
    const entry = { workspacePath: '/workspace', threads: [], errorMessage: 'prior timeout' }
    const params = { identity: { workspacePath: '/workspace' }, scope: 'workspace', includeSubAgents: true }
    const resolvers: Array<(result: { data: unknown[] }) => void> = []
    const onStarted = vi.fn((_client, method, requestParams) => beginWorkspaceThreadListRequest(entry, method, requestParams))
    const onCompleted = vi.fn((_client, method, requestParams, result, generation) =>
      applyWorkspaceThreadListRequestResult(entry, method, requestParams, result, generation)
    )
    const client = {
      sendRequest: vi.fn(() => {
        expect(onStarted).toHaveBeenCalledTimes(resolvers.length + 1)
        return new Promise<{ data: unknown[] }>((resolve) => resolvers.push(resolve))
      })
    }
    const handlers = registerHandlersForTest('/workspace', () => client as never, createIpcCallbacks({
      onAppServerRequestStarted: onStarted,
      onAppServerRequestCompleted: onCompleted
    }))
    const older = handlers.get('appserver:send-request')?.({}, 'thread/list', params)
    const newer = handlers.get('appserver:send-request')?.({}, 'thread/list', params)
    const result = { data: [{ id: 'existing' }, { id: 'newly-loaded' }] }

    resolvers[1](result)
    await expect(newer).resolves.toEqual(result)
    expect(entry.threads).toEqual(result.data)
    resolvers[0]({ data: [{ id: 'existing' }] })
    await older

    expect(entry.threads).toEqual(result.data)
    expect(entry.errorMessage).toBeUndefined()
    expect(onCompleted.mock.results.map((call) => call.value)).toEqual([true, false])
  })

  it('carries JSON-RPC code and data to the preload-side Error and drops a cyclic cause that cannot cross IPC', async () => {
    const rpcError = Object.assign(new Error('Workspace is busy.'), {
      name: 'JsonRpcError',
      code: 'jsonRpcError',
      rpcCode: -32101,
      data: {
        code: 'remote_workspace_busy',
        messageKey: 'error.remoteToolHost.workspaceBusy',
        params: { owner: 'other' },
        fallbackText: 'That folder is in use.'
      }
    })
    const sendRequest = vi.fn().mockRejectedValue(rpcError)
    const handlers = registerHandlersForTest('/workspace', () => ({ sendRequest } as never))

    const outcome = await handlers.get('appserver:send-request')?.({}, 'thread/list', {})

    try {
      unwrapAppServerResult(outcome)
      expect.unreachable('the envelope must reject')
    } catch (error) {
      const failure = error as Error & { code?: string; rpcCode?: number; data?: Record<string, unknown> }
      expect(failure).toBeInstanceOf(Error)
      expect(failure.message).toBe('Workspace is busy.')
      expect(failure.name).toBe('JsonRpcError')
      expect(failure.code).toBe('jsonRpcError')
      expect(failure.rpcCode).toBe(-32101)
      expect(failure.data).toEqual({
        code: 'remote_workspace_busy',
        messageKey: 'error.remoteToolHost.workspaceBusy',
        params: { owner: 'other' },
        fallbackText: 'That folder is in use.'
      })
    }

    const data: Record<string, unknown> = { code: 'remote_host_unavailable' }
    data.self = data
    const cyclicRequest = vi.fn().mockRejectedValue(
      Object.assign(new Error('Machine is offline.'), { rpcCode: -32100, data })
    )
    const cyclicHandlers = registerHandlersForTest('/workspace', () => ({ sendRequest: cyclicRequest } as never))

    const cyclicOutcome = await cyclicHandlers.get('appserver:send-request')?.({}, 'thread/list', {})

    expect(() => structuredClone(cyclicOutcome)).not.toThrow()
    try {
      unwrapAppServerResult(cyclicOutcome)
      expect.unreachable('the envelope must reject')
    } catch (error) {
      const failure = error as Error & { rpcCode?: number; data?: unknown }
      expect(failure.message).toBe('Machine is offline.')
      expect(failure.rpcCode).toBe(-32100)
      expect(failure.data).toBeUndefined()
    }
  })

  it('proxies Desktop Plugin App Surface GET requests through the resolved endpoint', async () => {
    const sendRequest = vi.fn().mockResolvedValue({
      appId: 'com.example.workflow',
      surfaceId: 'board',
      endpoint: 'http://127.0.0.1:5087/dotcraft/board/api/v1',
      bearer: 'surface-secret',
      expiresAt: '2099-01-01T00:00:00Z'
    })
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      text: vi.fn().mockResolvedValue('{"items":[1]}')
    })
    vi.stubGlobal('fetch', fetchMock)
    try {
      const handlers = registerHandlersForTest('/workspace', () => ({ sendRequest } as never))

      await expect(handlers.get('desktop-plugin:app-surface-get-json')?.({}, {
        appId: 'com.example.workflow',
        surfaceId: 'board',
        relativePath: '/items?state=open'
      })).resolves.toEqual({ items: [1] })

      expect(sendRequest).toHaveBeenCalledWith(
        'app/surface/resolve',
        { appId: 'com.example.workflow', surfaceId: 'board' },
        20_000
      )
      expect(fetchMock).toHaveBeenCalledWith(
        'http://127.0.0.1:5087/dotcraft/board/api/v1/items?state=open',
        expect.objectContaining({
          method: 'GET',
          headers: expect.objectContaining({ Authorization: 'Bearer surface-secret' })
        })
      )
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('posts Desktop Plugin App Surface JSON and rejects expired resolutions', async () => {
    const sendRequest = vi.fn()
      .mockResolvedValueOnce({
        appId: 'com.example.workflow',
        surfaceId: 'board',
        endpoint: 'http://localhost:5087/api',
        bearer: 'surface-secret',
        expiresAt: '2099-01-01T00:00:00Z'
      })
      .mockResolvedValueOnce({
        appId: 'com.example.workflow',
        surfaceId: 'board',
        endpoint: 'http://localhost:5087/api',
        bearer: 'surface-secret',
        expiresAt: '2000-01-01T00:00:00Z'
      })
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      text: vi.fn().mockResolvedValue('{}')
    })
    vi.stubGlobal('fetch', fetchMock)
    try {
      const handlers = registerHandlersForTest('/workspace', () => ({ sendRequest } as never))
      const request = {
        appId: 'com.example.workflow',
        surfaceId: 'board',
        relativePath: '/items',
        body: { title: 'Review' }
      }

      await expect(handlers.get('desktop-plugin:app-surface-post-json')?.({}, request)).resolves.toEqual({})
      expect(fetchMock).toHaveBeenCalledWith(
        'http://localhost:5087/api/items',
        expect.objectContaining({
          method: 'POST',
          body: '{"title":"Review"}'
        })
      )
      await expect(handlers.get('desktop-plugin:app-surface-post-json')?.({}, request))
        .rejects.toThrow('AppSurfaceUnavailable')
      expect(fetchMock).toHaveBeenCalledTimes(1)
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('rejects unsafe Desktop Plugin App Surface paths before fetching', async () => {
    const sendRequest = vi.fn().mockResolvedValue({
      appId: 'com.example.workflow',
      surfaceId: 'board',
      endpoint: 'http://127.0.0.1:5087/api',
      bearer: 'surface-secret',
      expiresAt: '2099-01-01T00:00:00Z'
    })
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    try {
      const handlers = registerHandlersForTest('/workspace', () => ({ sendRequest } as never))
      for (const relativePath of ['https://example.com/items', '//example.com/items', '/../items', '/%2e%2e/items', '/items#fragment', '/items\\child']) {
        await expect(handlers.get('desktop-plugin:app-surface-get-json')?.({}, {
          appId: 'com.example.workflow',
          surfaceId: 'board',
          relativePath
        }), relativePath).rejects.toThrow()
      }
      expect(fetchMock).not.toHaveBeenCalled()
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('enforces the Desktop Plugin App Surface response size limit', async () => {
    const sendRequest = vi.fn().mockResolvedValue({
      appId: 'com.example.workflow',
      surfaceId: 'board',
      endpoint: 'http://127.0.0.1:5087/api',
      bearer: 'surface-secret',
      expiresAt: '2099-01-01T00:00:00Z'
    })
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue({
      ok: true,
      status: 200,
      text: vi.fn().mockResolvedValue('x'.repeat(1_000_001))
    }))
    try {
      const handlers = registerHandlersForTest('/workspace', () => ({ sendRequest } as never))
      await expect(handlers.get('desktop-plugin:app-surface-get-json')?.({}, {
        appId: 'com.example.workflow',
        surfaceId: 'board',
        relativePath: '/items'
      })).rejects.toThrow('Response is too large')
    } finally {
      vi.unstubAllGlobals()
    }
  })

  it('opens only the native protocol declared by the selected app', async () => {
    const sendRequest = vi.fn().mockResolvedValue({
      apps: [{ appId: 'com.example.workflow', nativeApp: { protocol: 'workflow' } }]
    })
    const handlers = registerHandlersForTest('/workspace', () => ({ sendRequest } as never))

    await expect(handlers.get('desktop-plugin:app-open')?.({}, {
      appId: 'com.example.workflow',
      url: 'https://example.com/open'
    })).rejects.toThrow('App URL does not match the app native protocol.')

    await handlers.get('desktop-plugin:app-open')?.({}, {
      appId: 'com.example.workflow',
      url: 'workflow://open/board'
    })
    expect(shell.openExternal).toHaveBeenCalledWith('workflow://open/board')
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
      result,
      undefined
    )
  })

  it('never lets the renderer request the ChatGPT access token', async () => {
    const client = {
      sendRequest: vi.fn().mockResolvedValue({ isAuthenticated: true })
    }
    const handlers = registerHandlersForTest('/workspace', () => client as never)
    const params = { providerId: 'openai', includeToken: true, refreshToken: true }

    await handlers.get('appserver:send-request')?.({}, 'provider/auth/status', params)
    await handlers.get('appserver:send-request-raw')?.({}, 'provider/auth/status', params)

    expect(client.sendRequest).toHaveBeenCalledTimes(2)
    for (const call of client.sendRequest.mock.calls) {
      expect(call[1]).toMatchObject({ providerId: 'openai' })
      expect(call[1]).not.toHaveProperty('includeToken')
      expect(call[1]).not.toHaveProperty('refreshToken')
    }
  })

  it('identifies an AppServer request rejected before connection', async () => {
    const handlers = registerHandlersForTest('/workspace', () => null)

    const outcome = await handlers.get('appserver:send-request')?.({}, 'command/list', {
      privateValue: 'hidden'
    })

    expect(() => unwrapAppServerResult(outcome))
      .toThrow('AppServer is not connected (command/list)')
  })

  it('defers model listing until connected and forwards the trimmed provider id', async () => {
    await expect(
      registerHandlersForTest('/workspace', () => null).get('appserver:model-list')?.({}, 'openrouter')
    ).resolves.toBeNull()

    const result = { success: true, models: [{ id: 'model-1' }] }
    const sendRequest = vi.fn().mockResolvedValue(result)
    const handlers = registerHandlersForTest('/workspace', () => ({ sendRequest } as never))

    await expect(handlers.get('appserver:model-list')?.({}, ' openrouter ')).resolves.toBe(result)
    expect(sendRequest).toHaveBeenCalledWith('model/list', { providerId: 'openrouter' }, 20_000)
  })

  it('enriches raw Desktop thread starts with identity and existing runtime tools', async () => {
    const client = {
      sendRequest: vi.fn().mockResolvedValue({ thread: { id: 'thread-1' } })
    }
    const handlers = registerHandlersForTest('/workspace', () => client as never)

    await handlers.get('appserver:send-request-raw')?.(
      {},
      'thread/start',
      { displayName: 'Plugin thread' },
      20_000
    )

    expect(client.sendRequest).toHaveBeenCalledWith(
      'thread/start',
      expect.objectContaining({
        displayName: 'Plugin thread',
        identity: {
          channelName: 'dotcraft-desktop',
          userId: 'local',
          channelContext: 'workspace:/workspace',
          workspacePath: '/workspace'
        },
        dynamicTools: expect.any(Array)
      }),
      20_000
    )
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

  it('git:commit rejects outside paths and mismatched workspaces without running git', async () => {
    const commit = registerHandlersForTest().get('git:commit')!

    await expect(commit({}, '/workspace', ['/outside/secret.txt'], 'fix: outside')).rejects.toThrow('Access denied')
    await expect(commit({}, '/other-workspace', ['src/valid.ts'], 'fix: mismatch')).rejects.toThrow('Workspace path mismatch')
    expect(execFileMock).not.toHaveBeenCalled()
  })

  const crlfPatch = [
    'diff --git a/notes.txt b/notes.txt',
    'index 3b18e51..a9c7f3d 100644',
    '--- a/notes.txt',
    '+++ b/notes.txt',
    '@@ -1 +1 @@',
    '-hello\r',
    '+hello world\r',
    ''
  ].join('\n')
  const tempPatchFile = expect.stringMatching(/dotcraft-patch-.*\.diff$/)
  const exactBytesApply = ['-c', 'core.autocrlf=false', '-c', 'core.eol=lf', 'apply', '--whitespace=nowarn']

  it('git:applyPatch applies the patch from a temp file at the repository root', async () => {
    vi.mocked(fs.rm).mockResolvedValue(undefined)
    mockGitCommands((args) => {
      if (args[0] === 'rev-parse') return { stdout: '\n' }
      if (args.includes('apply')) return { stdout: '' }
      throw new Error(`Unexpected git command: ${args.join(' ')}`)
    })
    const handlers = registerHandlersForTest()
    const applyPatch = handlers.get('git:applyPatch')!

    await expect(applyPatch({}, '/workspace', crlfPatch, { reverse: false })).resolves.toEqual({ ok: true })

    const gitCalls = execFileMock.mock.calls.map(([, args]) => args as string[])
    expect(gitCalls).toEqual([
      ['rev-parse', '--show-prefix'],
      [...exactBytesApply, tempPatchFile]
    ])
    const patchFile = gitCalls[1].at(-1)
    expect(fs.writeFile).toHaveBeenCalledWith(patchFile, crlfPatch, 'utf8')
    expect(fs.rm).toHaveBeenCalledWith(patchFile, { force: true })
    expect(vi.mocked(fs.rm).mock.invocationCallOrder[0])
      .toBeGreaterThan(execFileMock.mock.invocationCallOrder[1])
  })

  it('git:applyPatch re-roots paths when the workspace is a repository subdirectory, before reversing', async () => {
    vi.mocked(fs.rm).mockResolvedValue(undefined)
    mockGitCommands((args) => (args[0] === 'rev-parse' ? { stdout: 'sub/\n' } : { stdout: '' }))
    const applyPatch = registerHandlersForTest().get('git:applyPatch')!

    await expect(applyPatch({}, '/workspace', crlfPatch, { reverse: false })).resolves.toEqual({ ok: true })
    await expect(applyPatch({}, '/workspace', crlfPatch, { reverse: true })).resolves.toEqual({ ok: true })

    expect(execFileMock.mock.calls[1][1]).toEqual([...exactBytesApply, '--directory=sub/', tempPatchFile])
    expect(execFileMock.mock.calls[3][1]).toEqual([...exactBytesApply, '--directory=sub/', '-R', tempPatchFile])
  })

  it('git:applyPatch reports a workspace outside git, and a patch that does not apply after removing the temp file', async () => {
    mockGitCommands(() => ({
      error: gitError(128),
      stderr: 'fatal: not a git repository (or any of the parent directories): .git\n'
    }))
    const handlers = registerHandlersForTest()
    const applyPatch = handlers.get('git:applyPatch')!

    await expect(applyPatch({}, '/workspace', crlfPatch, { reverse: false })).resolves.toEqual({
      ok: false,
      code: 'not-git-repo',
      message: 'fatal: not a git repository (or any of the parent directories): .git'
    })
    expect(execFileMock.mock.calls.map(([, args]) => args)).toEqual([['rev-parse', '--show-prefix']])
    expect(fs.writeFile).not.toHaveBeenCalled()

    execFileMock.mockClear()
    vi.mocked(fs.rm).mockResolvedValue(undefined)
    mockGitCommands((args) => (args[0] === 'rev-parse'
      ? { stdout: '\n' }
      : { error: gitError(1), stderr: 'error: patch failed: notes.txt:1\nerror: notes.txt: patch does not apply\n' }))
    await expect(applyPatch({}, '/workspace', crlfPatch, { reverse: true })).resolves.toEqual({
      ok: false,
      code: 'apply-failed',
      message: 'error: patch failed: notes.txt:1\nerror: notes.txt: patch does not apply'
    })
    expect(fs.rm).toHaveBeenCalledWith(execFileMock.mock.calls[1][1].at(-1), { force: true })
  })

  it('git:applyPatch rejects a blank patch or a mismatched workspace without running git', async () => {
    const applyPatch = registerHandlersForTest().get('git:applyPatch')!

    await expect(applyPatch({}, '/workspace', ' \n', { reverse: false })).resolves.toMatchObject({
      ok: false,
      code: 'invalid-patch'
    })
    await expect(
      applyPatch({}, '/other-workspace', crlfPatch, { reverse: false })
    ).rejects.toThrow('Workspace path mismatch')
    expect(execFileMock).not.toHaveBeenCalled()
    expect(fs.writeFile).not.toHaveBeenCalled()
  })

  it('git:inspectHead reads the branch of a known local project and its managed worktrees only', async () => {
    mockGitCommands((args) => {
      if (args[0] === 'rev-parse' && args[1] === '--is-inside-work-tree') return { stdout: 'true\n' }
      if (args[0] === 'branch' && args[1] === '--show-current') return { stdout: 'feature/details-card\n' }
      throw new Error(`Unexpected git command: ${args.join(' ')}`)
    })
    const handlers = registerHandlersForTest(
      '/workspace',
      () => null,
      createIpcCallbacks({
        getLocalProjectPaths: vi.fn(() => ['/recent/project'])
      })
    )
    const inspectHead = handlers.get('git:inspectHead')!

    await expect(inspectHead({}, '/recent/project')).resolves.toEqual({
      kind: 'branch',
      label: 'feature/details-card'
    })
    expect(execFileMock).toHaveBeenCalledWith(
      'git',
      ['rev-parse', '--is-inside-work-tree'],
      expect.objectContaining({ cwd: path.resolve('/recent/project') }),
      expect.any(Function)
    )
    await expect(
      inspectHead({}, '/recent/project/.craft/worktrees/details')
    ).resolves.toEqual({ kind: 'branch', label: 'feature/details-card' })
    await expect(inspectHead({}, '/recent/project/private')).rejects.toThrow('Workspace path mismatch')
  })

  it('git:inspectHead reports detached HEAD without granting arbitrary paths', async () => {
    mockGitCommands((args) => {
      if (args[0] === 'rev-parse' && args[1] === '--is-inside-work-tree') return { stdout: 'true\n' }
      if (args[0] === 'branch' && args[1] === '--show-current') return { stdout: '\n' }
      if (args[0] === 'rev-parse' && args[1] === '--short') return { stdout: 'abc1234\n' }
      throw new Error(`Unexpected git command: ${args.join(' ')}`)
    })
    const handlers = registerHandlersForTest()
    const inspectHead = handlers.get('git:inspectHead')!

    await expect(inspectHead({}, '/workspace')).resolves.toEqual({
      kind: 'detached',
      label: 'abc1234'
    })
    await expect(inspectHead({}, '/outside/project')).rejects.toThrow('Workspace path mismatch')
  })

  it('git:branch and git:listBranches read branches through git commands', async () => {
    mockGitCommands((args) => {
      if (args[0] === 'rev-parse' && args[1] === '--is-inside-work-tree') return { stdout: 'true\n' }
      if (args[0] === 'branch' && args[1] === '--show-current') return { stdout: 'feat/worktree\n' }
      if (args[0] === 'for-each-ref') return { stdout: 'main\nfeat/worktree\n' }
      throw new Error(`Unexpected git command: ${args.join(' ')}`)
    })
    const handlers = registerHandlersForTest()
    const listBranches = handlers.get('git:listBranches')!

    await expect(handlers.get('git:branch')!({}, '/workspace/.craft/worktrees/feat-worktree')).resolves.toBe('feat/worktree')
    expect(fs.readFile).not.toHaveBeenCalled()
    await expect(listBranches({}, '/workspace')).resolves.toEqual({
      current: 'feat/worktree',
      detachedHead: null,
      branches: [
        { name: 'main', current: false },
        { name: 'feat/worktree', current: true }
      ]
    })
  })

  it('git:checkoutBranch switches branches in the workspace or its managed worktrees only', async () => {
    mockGitCommands((args) => {
      if (args[0] === 'switch') return { stdout: '' }
      throw new Error(`Unexpected git command: ${args.join(' ')}`)
    })
    const checkout = registerHandlersForTest().get('git:checkoutBranch')!

    await expect(checkout({}, '/outside/worktree', 'main')).rejects.toThrow('Workspace path mismatch')
    expect(execFileMock).not.toHaveBeenCalled()
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

  it('workspace:create-local-project creates a Documents project, git-inits it, and returns the path', async () => {
    existsSyncMock.mockReturnValue(false)
    mockGitCommands((args) => {
      if (args[0] === 'init') return { stdout: '' }
      throw new Error(`Unexpected git command: ${args.join(' ')}`)
    })
    const handlers = registerHandlersForTest()
    const create = handlers.get('workspace:create-local-project')!

    const result = await create({}, { name: 'My App' }) as { path: string; gitInitialized: boolean }

    const expected = path.join('C:\\Users\\tester', 'My App')
    expect(result).toEqual({ path: expected, gitInitialized: true })
    expect(fs.mkdir).toHaveBeenCalledWith(expected, { recursive: true })
    expect(execFileMock).toHaveBeenCalledWith(
      'git',
      ['init'],
      expect.objectContaining({ cwd: expected }),
      expect.any(Function)
    )
  })

  it('workspace:create-local-project disambiguates an existing folder and survives missing git', async () => {
    const taken = path.join('C:\\Users\\tester', 'My App')
    existsSyncMock.mockImplementation((candidate: string) => String(candidate) === taken)
    mockGitCommands(() => ({ error: gitError(1, 'git not found') }))
    const create = registerHandlersForTest().get('workspace:create-local-project')!

    await expect(create({}, { name: 'My App' })).resolves.toEqual({
      path: path.join('C:\\Users\\tester', 'My App 2'),
      gitInitialized: false
    })
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

  it('chrome:install-native-host runs only the bundled installer and chrome:open forwards only normalized targets', async () => {
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

    execFileMock.mockClear()
    execFileMock.mockImplementation((_command, _args, _options, callback: ExecFileCallback) => {
      callback(null, '{"ok":true,"opened":true}', '')
      return null
    })
    const openChrome = handlers.get('chrome:open')!

    await openChrome({}, { url: 'chrome://extensions' })
    await openChrome({}, { url: 'file:///C:/secret.txt' })

    expect(execFileMock).toHaveBeenCalledTimes(2)
    const firstArgs = execFileMock.mock.calls[0]![1] as string[]
    const secondArgs = execFileMock.mock.calls[1]![1] as string[]
    expect(firstArgs.at(-1)).toBe('chrome://extensions/?id=pekajfcokkicggfjmickmkngmmoojlda')
    expect(secondArgs.at(-1)).toBe('about:blank')
  })

  it('rejects unsafe open target requests, then opens existing paths at their position and reveals them', async () => {
    const handlers = registerHandlersForTest()
    const open = handlers.get('open-targets:open')!

    await expect(open({}, { targetId: 'vscode', path: 'relative/path.txt' })).rejects.toThrow(
      'Local path must be absolute'
    )
    await expect(open({}, { targetId: 'vscode', path: 'file:///tmp/path.txt' })).rejects.toThrow(
      'Local path must be an absolute filesystem path'
    )

    vi.mocked(fs.stat).mockRejectedValueOnce(Object.assign(new Error('missing'), { code: 'ENOENT' }))
    await expect(open({}, { targetId: 'vscode', path: path.resolve('/tmp/missing.txt') })).rejects.toThrow('missing')

    expect(openInTargetMock).not.toHaveBeenCalled()

    const targetPath = path.resolve('/tmp/dotcraft-local-note.txt')
    vi.mocked(fs.stat).mockResolvedValue({} as Awaited<ReturnType<typeof fs.stat>>)

    await handlers.get('open-targets:open')?.({}, { targetId: 'vscode', path: targetPath, line: 4, column: 2 })
    await handlers.get('shell:reveal-local-path')?.({}, targetPath)

    expect(openInTargetMock).toHaveBeenCalledWith({ targetId: 'vscode', path: targetPath, line: 4, column: 2 })
    expect(shell.showItemInFolder).toHaveBeenCalledWith(targetPath)
  })

  it('remote stack Open in Desktop activates a saved stack without persisting a localhost remote URL', async () => {
    const handlers = new Map<string, (...args: unknown[]) => unknown>()
    vi.mocked(ipcMain.handle).mockImplementation((channel, handler) => {
      handlers.set(channel, handler as (...args: unknown[]) => unknown)
    })
    const updateSettings = vi.fn()
    const onConnectDockerDeployment = vi.fn().mockResolvedValue({ localPort: 51523 })
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
        oratorioPort: 5087,
        dashboardPort: 8080,
      }]
    }

    registerIpcHandlers(null, () => null, '/workspace', createIpcCallbacks({
      getSettings: vi.fn(() => ({ remoteHosts: normalizeSshMachines([host]) })),
      getSshMachinesManager: () => machinesManagerFor([host]),
      updateSettings,
      onConnectDockerDeployment
    }))

    const result = await handlers.get('sshMachines:docker-open')?.({}, {
      id: 'h1',
      stackId: 's1'
    })

    expect(onConnectDockerDeployment).toHaveBeenCalledWith(
      expect.objectContaining({ id: 'h1' }),
      expect.objectContaining({ id: 's1' })
    )
    expect(updateSettings).not.toHaveBeenCalledWith(
      expect.objectContaining({
        connectionMode: 'remote',
        remote: expect.objectContaining({ url: expect.stringContaining('127.0.0.1') })
      })
    )
    expect(result).toEqual({ ok: true, machineId: 'h1', stackId: 's1', localPort: 51523 })
  })

  it('modules:read-config parses BOM-prefixed JSON and rethrows invalid JSON', async () => {
    vi.mocked(fs.stat).mockResolvedValue({ size: 32 } as Awaited<ReturnType<typeof fs.stat>>)
    vi.mocked(fs.readFile)
      .mockResolvedValueOnce('\uFEFF{"Enabled":true}' as Awaited<ReturnType<typeof fs.readFile>>)
      .mockResolvedValueOnce('{invalid-json' as Awaited<ReturnType<typeof fs.readFile>>)
    const readConfig = registerHandlersForTest().get('modules:read-config')!

    await expect(readConfig({}, { configFileName: 'module.json' })).resolves.toEqual({
      exists: true,
      config: { Enabled: true }
    })
    await expect(readConfig({}, { configFileName: 'module.json' })).rejects.toThrow()
  })

  describe('modules:start', () => {
    function scanDemoModule(configDescriptors: unknown[] = []): void {
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
          configDescriptors
        }
      ])
    }

    it('refuses invalid or non-object config JSON without overwriting the file', async () => {
      scanDemoModule()
      vi.mocked(fs.readFile)
        .mockResolvedValueOnce('{invalid-json' as Awaited<ReturnType<typeof fs.readFile>>)
        .mockResolvedValueOnce('["not-an-object"]' as Awaited<ReturnType<typeof fs.readFile>>)
      const start = registerHandlersForTest().get('modules:start')!

      await expect(start({}, { moduleId: 'demo-module' })).resolves.toMatchObject({ ok: false })
      await expect(start({}, { moduleId: 'demo-module' })).resolves.toEqual({
        ok: false,
        error: 'Config payload must be a JSON object'
      })
      expect(vi.mocked(fs.writeFile)).not.toHaveBeenCalled()
      expect(channelModuleManagerStartMock).not.toHaveBeenCalled()
    })

    it('reports missing required fields when no config file exists', async () => {
      scanDemoModule([
        {
          key: 'demo.botToken',
          displayLabel: 'Bot Token',
          description: '',
          required: true,
          dataKind: 'string',
          masked: true,
          interactiveSetupOnly: false
        }
      ])
      vi.mocked(fs.readFile).mockRejectedValue(Object.assign(new Error('ENOENT'), { code: 'ENOENT' }))

      await expect(
        registerHandlersForTest().get('modules:start')!({}, { moduleId: 'demo-module' })
      ).resolves.toMatchObject({ ok: false, missingFields: ['Bot Token'] })
      expect(channelModuleManagerStartMock).not.toHaveBeenCalled()
    })

    it('creates the config file when nothing is left to fill in', async () => {
      scanDemoModule()
      vi.mocked(fs.readFile).mockRejectedValue(Object.assign(new Error('ENOENT'), { code: 'ENOENT' }))

      await expect(
        registerHandlersForTest().get('modules:start')!({}, { moduleId: 'demo-module' })
      ).resolves.toMatchObject({ ok: true })
      expect(vi.mocked(fs.writeFile)).toHaveBeenCalled()
      expect(channelModuleManagerStartMock).toHaveBeenCalledWith('demo-module')
    })
  })

  it('awaits async updateSettings in settings:set handler', async () => {
    let resolveUpdate: (() => void) | null = null
    const updateSettings = vi.fn(() => new Promise<void>((resolve) => { resolveUpdate = resolve }))
    const settingsSet = registerHandlersForTest('/workspace', () => null, createIpcCallbacks({ updateSettings })).get('settings:set')

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

  describe('chat folders', () => {
    const chatsWorkspace = resolveDefaultChatWorkspacePath()
    const chatsRoot = path.resolve('/documents/DotCraft')
    const chatFolder = path.join(chatsRoot, '2026-10-11', 'plan-a-trip')
    const dateFolder = path.join(chatsRoot, '2026-10-11')

    afterEach(() => setLocalChatsFolderRootsResolver(() => []))

    it('opens a chat folder to search, git inspection, reveal, and the terminal while Chats is the local foreground', async () => {
      setLocalChatsFolderRootsResolver(() => [chatsRoot])
      mockGitCommands(() => ({ error: gitError(128, 'not a git repository') }))
      const handlers = registerHandlersForTest(chatsWorkspace)
      const report = path.join(chatFolder, 'outputs', 'report.md')

      await handlers.get('workspace:search-files')!({}, { query: 'report', workspacePath: chatFolder, limit: 5 })
      expect(listWorkspaceFilesMock).toHaveBeenCalledWith(chatFolder, 'report', 5)
      await expect(handlers.get('git:inspectHead')!({}, chatFolder)).resolves.toEqual({ kind: 'none' })
      await handlers.get('shell:show-item-in-folder')!({}, report)
      expect(shell.showItemInFolder).toHaveBeenCalledWith(report)
      expect(isTerminalCwdAllowed(chatsWorkspace, path.join(chatFolder, 'work'))).toBe(true)

      await expect(handlers.get('workspace:search-files')!({}, { query: 'x', workspacePath: dateFolder })).rejects.toThrow('Workspace path mismatch')
      await expect(handlers.get('git:inspectHead')!({}, chatsRoot)).rejects.toThrow('Workspace path mismatch')
      await expect(handlers.get('shell:show-item-in-folder')!({}, path.join(chatsRoot, 'loose.md'))).rejects.toThrow()
      expect(isTerminalCwdAllowed(chatsWorkspace, dateFolder)).toBe(false)
    })

    it('reads files inside a chat folder but nowhere else outside the workspace', async () => {
      setLocalChatsFolderRootsResolver(() => [chatsRoot])
      vi.mocked(fs.readFile).mockResolvedValueOnce('# Report' as never)
      const handlers = registerHandlersForTest(chatsWorkspace)
      const report = path.join(chatFolder, 'outputs', 'report.md')

      await expect(handlers.get('file:read')!({}, report)).resolves.toBe('# Report')
      expect(fs.readFile).toHaveBeenCalledWith(report, 'utf-8')

      await expect(handlers.get('file:read')!({}, path.join(chatsRoot, 'loose.md'))).rejects.toThrow()
      await expect(handlers.get('file:exists')!({}, path.resolve('/elsewhere/report.md'))).rejects.toThrow()
    })

    it('keeps chat folders closed for a project workspace or when Chats is not the local foreground', async () => {
      setLocalChatsFolderRootsResolver(() => [chatsRoot])
      const project = registerHandlersForTest('/workspace')
      await expect(project.get('workspace:search-files')!({}, { query: 'x', workspacePath: chatFolder })).rejects.toThrow('Workspace path mismatch')
      await expect(project.get('git:inspectHead')!({}, chatFolder)).rejects.toThrow('Workspace path mismatch')
      expect(isTerminalCwdAllowed('/workspace', chatFolder)).toBe(false)

      setLocalChatsFolderRootsResolver(() => [])
      const chats = registerHandlersForTest(chatsWorkspace)
      await expect(chats.get('workspace:search-files')!({}, { query: 'x', workspacePath: chatFolder })).rejects.toThrow('Workspace path mismatch')
      expect(isTerminalCwdAllowed(chatsWorkspace, chatFolder)).toBe(false)
      expect(listWorkspaceFilesMock).not.toHaveBeenCalled()
    })
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

  it('shows task completion notifications per mode, defaulting to unfocused only', () => {
    expect(shouldShowTaskCompletionNotification(createWindow(false))).toBe(true)
    expect(shouldShowTaskCompletionNotification(createWindow(true))).toBe(false)
    expect(shouldShowTaskCompletionNotification(createWindow(true), {
      notifications: { taskCompletionMode: 'always' }
    })).toBe(true)
    expect(shouldShowTaskCompletionNotification(createWindow(false), {
      notifications: { taskCompletionMode: 'never' }
    })).toBe(false)
  })

  it('shows or suppresses native job result notifications per settings while always forwarding renderer events', () => {
    const shown = createWindow(true)
    broadcastNotification(shown, 'system/jobResult', {
      jobName: 'Scheduled task',
      result: '**Done** with `task`'
    }, {
      notifications: { taskCompletionMode: 'always' }
    })

    expect(Notification).toHaveBeenCalledWith({
      title: 'Scheduled task',
      body: 'Done with task'
    })
    expect(notificationShowMock).toHaveBeenCalledOnce()
    expect(shown.webContents.send).toHaveBeenCalledWith('appserver:notification', {
      method: 'system/jobResult',
      params: {
        jobName: 'Scheduled task',
        result: '**Done** with `task`'
      }
    })

    vi.mocked(Notification).mockClear()
    const suppressed = createWindow(false)
    broadcastNotification(suppressed, 'system/jobResult', { jobName: 'Automation', result: 'Done' }, {
      notifications: { taskCompletionMode: 'never' }
    })

    expect(Notification).not.toHaveBeenCalled()
    expect(suppressed.webContents.send).toHaveBeenCalledWith('appserver:notification', {
      method: 'system/jobResult',
      params: { jobName: 'Automation', result: 'Done' }
    })
  })

  it('tags forwarded notifications with workspace and foreground state and forwards interactive requests without a native notification', () => {
    const win = createWindow(false)
    const params = { threadId: 'thread_1', runtime: { running: true } }

    broadcastNotification(win, 'thread/status/changed', params, undefined, 'F:/workspace-b')
    broadcastNotification(win, 'thread/status/changed', params, undefined, 'F:/workspace-b', false)

    expect(win.webContents.send).toHaveBeenNthCalledWith(1, 'appserver:notification', {
      method: 'thread/status/changed',
      params,
      workspacePath: 'F:/workspace-b'
    })
    expect(win.webContents.send).toHaveBeenNthCalledWith(2, 'appserver:notification', {
      method: 'thread/status/changed',
      params,
      workspacePath: 'F:/workspace-b',
      foreground: false
    })

    const payload = { bridgeId: 'bridge-1', method: 'item/approval/request', params: { reason: 'Run a command' } }
    broadcastServerRequest(win, payload)
    expect(Notification).not.toHaveBeenCalled()
    expect(win.webContents.send).toHaveBeenLastCalledWith('appserver:server-request', payload)
  })
})

describe('unregisterIpcHandlers', () => {
  it('removes each workspace project handler exactly once', () => {
    vi.clearAllMocks()
    registerHandlersForTest('/workspace', () => null, createIpcCallbacks({ clearProjects: vi.fn() }))
    vi.mocked(ipcMain.removeHandler).mockClear()

    unregisterIpcHandlers()

    const removedChannels = vi.mocked(ipcMain.removeHandler).mock.calls.map(([channel]) => channel)
    for (const channel of ['workspace:get-projects', 'workspace:remove-project', 'workspace:disconnect-remote', 'workspace:clear-projects']) {
      expect(removedChannels.filter((candidate) => candidate === channel)).toHaveLength(1)
    }
  })
})

describe('DesktopAppServerClient bidirectional routing', () => {
  async function respondToServerRequest(
    handler: Parameters<DesktopAppServerClient['onServerRequest']>[0],
    request: Record<string, unknown>
  ): Promise<Record<string, unknown>> {
    const toServer = new PassThrough()
    const fromServer = new PassThrough()
    const client = new DesktopAppServerClient(fromServer as unknown as Readable, toServer as unknown as Writable)
    const response = new Promise<Record<string, unknown>>((resolve) => {
      toServer.on('data', (chunk: Buffer) => {
        for (const line of chunk.toString('utf8').split('\n').filter(Boolean)) {
          const message = JSON.parse(line)
          if (message.id === request.id && ('result' in message || 'error' in message)) resolve(message)
        }
      })
    })
    client.onServerRequest(handler)
    fromServer.push(JSON.stringify({ jsonrpc: '2.0', ...request }) + '\n')
    try {
      return await response
    } finally {
      client.dispose()
      toServer.destroy()
      fromServer.destroy()
    }
  }

  it('sends the handler result back as a JSON-RPC response with the original id', async () => {
    const response = await respondToServerRequest(
      async (_method, params) => ({ decision: (params as Record<string, unknown>).defaultDecision ?? 'accept' }),
      {
        id: 42,
        method: 'item/approval/request',
        params: { approvalType: 'shell', operation: 'rm -rf /tmp', defaultDecision: 'decline' }
      }
    )

    expect(response).toMatchObject({ jsonrpc: '2.0', id: 42, result: { decision: 'decline' } })
  })

  it('sends an internal error response when the handler throws', async () => {
    const response = await respondToServerRequest(
      async () => { throw new Error('Bridge unavailable') },
      { id: 77, method: 'item/approval/request', params: {} }
    )

    expect((response.error as { code: number }).code).toBe(-32603)
  })
})

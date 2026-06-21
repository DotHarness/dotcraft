import { beforeEach, describe, expect, it, vi } from 'vitest'
import { findSseBoundary, HubClient } from '../HubClient'

const childProcessMocks = vi.hoisted(() => ({
  spawn: vi.fn(() => ({ unref: vi.fn() }))
}))

const fsMocks = vi.hoisted(() => ({
  existsSync: vi.fn(() => true),
  readFileSync: vi.fn(() => JSON.stringify({
    pid: 1234,
    apiBaseUrl: 'http://127.0.0.1:8123',
    token: 'hub-token',
    startedAt: '',
    version: ''
  }))
}))

vi.mock('child_process', () => childProcessMocks)
vi.mock('fs', () => fsMocks)
vi.mock('os', () => ({
  homedir: () => 'C:/Users/test'
}))

describe('HubClient SSE parsing', () => {
  it('detects LF and CRLF event boundaries', () => {
    expect(findSseBoundary('data: {}\n\n')?.sequence).toBe('\n\n')
    expect(findSseBoundary('data: {}\r\n\r\n')?.sequence).toBe('\r\n\r\n')
  })
})

describe('HubClient AppServer management', () => {
  beforeEach(() => {
    vi.restoreAllMocks()
    childProcessMocks.spawn.mockClear()
    childProcessMocks.spawn.mockReturnValue({ unref: vi.fn() })
    fsMocks.existsSync.mockReturnValue(true)
    fsMocks.readFileSync.mockReturnValue(JSON.stringify({
      pid: 1234,
      apiBaseUrl: 'http://127.0.0.1:8123',
      token: 'hub-token',
      startedAt: '',
      version: ''
    }))
    vi.spyOn(process, 'kill').mockImplementation(() => true)
  })

  it('sends runtime tool hints when ensuring AppServer', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          workspacePath: 'E:/repo',
          canonicalWorkspacePath: 'E:/repo',
          state: 'running',
          endpoints: {
            appServerWebSocket: 'ws://127.0.0.1:9000/ws'
          },
          serviceStatus: {},
          startedByHub: true
        })
      })
    vi.stubGlobal('fetch', fetchMock)

    await new HubClient().ensureAppServer('E:/repo', {
      runtimeTools: { ripgrepPath: 'C:/App/resources/rg.exe' }
    })

    const ensureInit = fetchMock.mock.calls[1][1] as RequestInit
    expect(JSON.parse(String(ensureInit.body))).toMatchObject({
      workspacePath: 'E:/repo',
      runtimeTools: { ripgrepPath: 'C:/App/resources/rg.exe' }
    })
  })

  it('sends runtime tools when restarting AppServer', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({
        ok: true,
        json: async () => ({
          workspacePath: 'E:/repo',
          canonicalWorkspacePath: 'E:/repo',
          state: 'running',
          endpoints: {
            appServerWebSocket: 'ws://127.0.0.1:9000/ws'
          },
          serviceStatus: {},
          startedByHub: true
        })
      })
    vi.stubGlobal('fetch', fetchMock)

    await new HubClient().restartAppServer('E:/repo', { ripgrepPath: 'C:/App/resources/rg.exe' })

    const restartInit = fetchMock.mock.calls[1][1] as RequestInit
    expect(JSON.parse(String(restartInit.body))).toMatchObject({
      workspacePath: 'E:/repo',
      runtimeTools: { ripgrepPath: 'C:/App/resources/rg.exe' }
    })
  })

  it('includes Hub error details in thrown errors', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({
        ok: false,
        status: 500,
        json: async () => ({
          error: {
            code: 'appServerStartFailed',
            message: 'Managed AppServer failed during startup.',
            details: {
              workspacePath: 'E:/repo',
              error: 'Access denied while binding WebSocket.',
              recentStderr: 'System.Net.Sockets.SocketException: forbidden'
            }
          }
        })
      })
    vi.stubGlobal('fetch', fetchMock)

    await expect(new HubClient().ensureAppServer('E:/repo')).rejects.toMatchObject({
      code: 'appServerStartFailed',
      details: expect.objectContaining({
        error: 'Access denied while binding WebSocket.'
      }),
      message: expect.stringContaining('Access denied while binding WebSocket.')
    })
  })

  it('uses structured Hub internal error messages instead of generic HTTP 500 text', async () => {
    const fetchMock = vi.fn()
      .mockResolvedValueOnce({ ok: true })
      .mockResolvedValueOnce({
        ok: false,
        status: 500,
        json: async () => ({
          error: {
            code: 'hubInternalError',
            message: 'Hub encountered an unexpected internal error.',
            details: {
              type: 'IOException'
            }
          }
        })
      })
    vi.stubGlobal('fetch', fetchMock)

    let thrown: unknown
    try {
      await new HubClient().ensureAppServer('E:/repo')
    } catch (error) {
      thrown = error
    }

    expect(thrown).toMatchObject({
      code: 'hubInternalError',
      details: expect.objectContaining({
        type: 'IOException'
      }),
      message: expect.stringContaining('Hub encountered an unexpected internal error.')
    })
    expect((thrown as Error).message).not.toContain('Hub request failed with HTTP 500.')
  })

  it('restarts a live Hub when dev mode expects a different binary', async () => {
    const expectedBinary = 'C:/repo/build/release/dotcraft.exe'
    let hubPid = 1234
    let hubBinary = 'C:/Program Files/DotCraft/resources/bin/dotcraft.exe'
    let shutdownRequested = false

    fsMocks.readFileSync.mockImplementation(() => JSON.stringify({
      pid: hubPid,
      apiBaseUrl: 'http://127.0.0.1:8123',
      token: 'hub-token',
      startedAt: '',
      version: '',
      binaryPath: hubBinary
    }))
    vi.spyOn(process, 'kill').mockImplementation((pid) => {
      if (pid === 1234 && shutdownRequested) {
        const error = new Error('missing') as NodeJS.ErrnoException
        error.code = 'ESRCH'
        throw error
      }
      return true
    })

    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url.endsWith('/v1/status')) {
        return {
          ok: true,
          json: async () => ({ binaryPath: hubBinary })
        } as Response
      }
      if (url.endsWith('/v1/shutdown')) {
        shutdownRequested = true
        hubPid = 5678
        hubBinary = expectedBinary
        return {
          ok: true,
          json: async () => ({ ok: true })
        } as Response
      }
      if (url.endsWith('/v1/appservers/ensure')) {
        return {
          ok: true,
          json: async () => ({
            workspacePath: 'E:/repo',
            canonicalWorkspacePath: 'E:/repo',
            state: 'running',
            endpoints: { appServerWebSocket: 'ws://127.0.0.1:9000/ws' },
            serviceStatus: {},
            startedByHub: true
          })
        } as Response
      }
      throw new Error(`unexpected fetch: ${url} ${String(init?.method)}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    await new HubClient({
      binarySource: 'custom',
      binaryPath: expectedBinary,
      restartMismatchedHub: true
    }).ensureAppServer('E:/repo')

    expect(childProcessMocks.spawn).toHaveBeenCalledWith(
      expectedBinary,
      ['hub'],
      expect.objectContaining({ detached: true })
    )
    expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/v1/shutdown'))).toBe(true)
  })

  it('restarts a live Hub by default when it reports a different binary', async () => {
    const expectedBinary = 'C:/Users/test/AppData/Local/Programs/DotCraft/resources/bin/dotcraft.exe'
    let hubPid = 1234
    let hubBinary = 'C:/Users/test/AppData/Local/Programs/DotCraft-old/resources/bin/dotcraft.exe'
    let shutdownRequested = false

    fsMocks.readFileSync.mockImplementation(() => JSON.stringify({
      pid: hubPid,
      apiBaseUrl: 'http://127.0.0.1:8123',
      token: 'hub-token',
      startedAt: '',
      version: '',
      binaryPath: hubBinary
    }))
    vi.spyOn(process, 'kill').mockImplementation((pid) => {
      if (pid === 1234 && shutdownRequested) {
        const error = new Error('missing') as NodeJS.ErrnoException
        error.code = 'ESRCH'
        throw error
      }
      return true
    })

    const fetchMock = vi.fn(async (url: string, init?: RequestInit) => {
      if (url.endsWith('/v1/status')) {
        return {
          ok: true,
          json: async () => ({ binaryPath: hubBinary })
        } as Response
      }
      if (url.endsWith('/v1/shutdown')) {
        shutdownRequested = true
        hubPid = 5678
        hubBinary = expectedBinary
        return {
          ok: true,
          json: async () => ({ ok: true })
        } as Response
      }
      if (url.endsWith('/v1/appservers/ensure')) {
        return {
          ok: true,
          json: async () => ({
            workspacePath: 'E:/repo',
            canonicalWorkspacePath: 'E:/repo',
            state: 'running',
            endpoints: { appServerWebSocket: 'ws://127.0.0.1:9000/ws' },
            serviceStatus: {},
            startedByHub: true
          })
        } as Response
      }
      throw new Error(`unexpected fetch: ${url} ${String(init?.method)}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    await new HubClient({
      binarySource: 'custom',
      binaryPath: expectedBinary
    }).ensureAppServer('E:/repo')

    expect(childProcessMocks.spawn).toHaveBeenCalledWith(
      expectedBinary,
      ['hub'],
      expect.objectContaining({ detached: true })
    )
    expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/v1/shutdown'))).toBe(true)
  })

  it('reuses a live Hub when the binary already matches', async () => {
    const expectedBinary = 'C:/repo/build/release/dotcraft.exe'
    fsMocks.readFileSync.mockReturnValue(JSON.stringify({
      pid: 1234,
      apiBaseUrl: 'http://127.0.0.1:8123',
      token: 'hub-token',
      startedAt: '',
      version: '',
      binaryPath: expectedBinary
    }))
    const fetchMock = vi.fn(async (url: string) => {
      if (url.endsWith('/v1/status')) {
        return {
          ok: true,
          json: async () => ({ binaryPath: expectedBinary })
        } as Response
      }
      if (url.endsWith('/v1/appservers/ensure')) {
        return {
          ok: true,
          json: async () => ({
            workspacePath: 'E:/repo',
            canonicalWorkspacePath: 'E:/repo',
            state: 'running',
            endpoints: { appServerWebSocket: 'ws://127.0.0.1:9000/ws' },
            serviceStatus: {},
            startedByHub: true
          })
        } as Response
      }
      throw new Error(`unexpected fetch: ${url}`)
    })
    vi.stubGlobal('fetch', fetchMock)

    await new HubClient({
      binarySource: 'custom',
      binaryPath: expectedBinary,
      restartMismatchedHub: true
    }).ensureAppServer('E:/repo')

    expect(childProcessMocks.spawn).not.toHaveBeenCalled()
    expect(fetchMock.mock.calls.some(([url]) => String(url).endsWith('/v1/shutdown'))).toBe(false)
  })
})

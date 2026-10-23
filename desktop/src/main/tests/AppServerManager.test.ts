import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { EventEmitter } from 'events'
import { PassThrough } from 'stream'

vi.mock('child_process', () => ({
  spawn: vi.fn(),
  execFileSync: vi.fn(() => { throw new Error('not found') })
}))

vi.mock('fs', () => ({
  existsSync: vi.fn((p: string) => p === '/usr/bin/dotcraft')
}))

vi.mock('@vscode/ripgrep', () => ({
  rgPath: 'C:/DotCraft/resources/app.asar/node_modules/@vscode/ripgrep/bin/rg.exe'
}))

vi.mock('electron', () => ({
  app: {
    isPackaged: false,
    getAppPath: () => 'C:/DotCraft'
  }
}))

import { AppServerManager, resolveBinaryLocation } from '../AppServerManager'
import { APP_SERVER_READY_POLL_MS, waitForReadyz } from '../appServerReady'
import { spawn, execFileSync } from 'child_process'
import { existsSync } from 'fs'

function makeMockProcess(): {
  proc: EventEmitter & {
    stdin: PassThrough
    stdout: PassThrough
    killed: boolean
    exitCode: number | null
    kill: ReturnType<typeof vi.fn>
  }
} {
  const proc = new EventEmitter() as EventEmitter & {
    stdin: PassThrough
    stdout: PassThrough
    killed: boolean
    exitCode: number | null
    kill: ReturnType<typeof vi.fn>
  }
  proc.stdin = new PassThrough()
  proc.stdout = new PassThrough()
  proc.killed = false
  proc.exitCode = null
  proc.kill = vi.fn(() => {
    proc.killed = true
    proc.emit('exit', null, 'SIGKILL')
  })
  return { proc }
}

function makeMockProcessNoStdin(): {
  proc: EventEmitter & {
    stdin: null
    stdout: PassThrough
    killed: boolean
    exitCode: number | null
    kill: ReturnType<typeof vi.fn>
  }
} {
  const proc = new EventEmitter() as EventEmitter & {
    stdin: null
    stdout: PassThrough
    killed: boolean
    exitCode: number | null
    kill: ReturnType<typeof vi.fn>
  }
  proc.stdin = null
  proc.stdout = new PassThrough()
  proc.killed = false
  proc.exitCode = null
  proc.kill = vi.fn(() => {
    proc.killed = true
    proc.emit('exit', null, 'SIGKILL')
  })
  return { proc }
}

describe('AppServerManager', () => {
  let manager: AppServerManager
  const mockSpawn = spawn as ReturnType<typeof vi.fn>
  const mockExistsSync = existsSync as ReturnType<typeof vi.fn>
  const mockExecFileSync = execFileSync as ReturnType<typeof vi.fn>

  beforeEach(() => {
    vi.clearAllMocks()
    mockExistsSync.mockReset().mockReturnValue(true)
    manager = new AppServerManager({ workspacePath: '/home/user/project' })
  })

  afterEach(() => {
    manager.shutdown()
  })

  it('spawns with correct arguments and working directory', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)

    manager.spawn()

    expect(mockSpawn).toHaveBeenCalledWith(
      expect.any(String),
      ['app-server'],
      expect.objectContaining({
        cwd: '/home/user/project',
        env: expect.objectContaining({
          DOTCRAFT_RG_PATH: 'C:/DotCraft/resources/app.asar.unpacked/node_modules/@vscode/ripgrep/bin/rg.exe',
          DOTCRAFT_BUILTIN_PLUGIN_ROOTS: expect.stringMatching(/resources[\\/]plugins[\\/]dotcraft-bundled[\\/]plugins(?:$|[;:].*)/)
        }),
        stdio: ['pipe', 'pipe', 'inherit']
      })
    )
  })

  it.each([
    ['ws://127.0.0.1:9100', ['ignore', 'inherit', 'inherit']],
    ['ws+stdio://127.0.0.1:9100', ['pipe', 'pipe', 'inherit']]
  ])('passes --listen %s with the matching stdio pipes', (listenUrl, stdio) => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    manager = new AppServerManager({ workspacePath: '/home/user/project', listenUrl })

    manager.spawn()

    expect(mockSpawn).toHaveBeenCalledWith(
      expect.any(String),
      ['app-server', '--listen', listenUrl],
      expect.objectContaining({ cwd: '/home/user/project', stdio })
    )
  })

  it('emits "started", exposes stdio, and tracks running state', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    const started = vi.fn()
    manager.on('started', started)

    expect(manager.isRunning).toBe(false)
    manager.spawn()
    proc.emit('spawn')

    expect(started).toHaveBeenCalledOnce()
    expect(manager.isRunning).toBe(true)
    expect(manager.stdin).toBe(proc.stdin)
    expect(manager.stdout).toBe(proc.stdout)
    proc.emit('exit', 0, null)
    expect(manager.isRunning).toBe(false)
  })

  it('emits "error" when binary is not found', () => {
    mockExistsSync.mockReturnValue(false)
    mockExecFileSync.mockImplementation(() => { throw new Error('not found') })

    const errorHandler = vi.fn()
    manager.on('error', errorHandler)
    manager.spawn()

    expect(errorHandler).toHaveBeenCalledOnce()
    const err: Error = errorHandler.mock.calls[0][0]
    expect(err.message).toMatch(/not found/i)
  })

  it('resolves the PATH binary when source is set to path', () => {
    mockExistsSync.mockImplementation((p: string) => p === '/usr/bin/dotcraft')
    mockExecFileSync.mockReturnValue('/usr/bin/dotcraft\n')

    const resolved = resolveBinaryLocation({ binarySource: 'path' })

    expect(resolved).toEqual({
      source: 'path',
      path: '/usr/bin/dotcraft'
    })
  })

  it('uses only the repository build output when a dev build is required', () => {
    const devBuild = /build[\\/]+release[\\/]+dotcraft(\.exe)?$/i
    const options = { binarySource: 'bundled' as const, preferDevBuild: true, requireDevBuild: true }
    mockExistsSync.mockImplementation((p: string) => p.endsWith('dotcraft.sln') || devBuild.test(p))
    expect(resolveBinaryLocation(options).path).toMatch(devBuild)

    mockExistsSync.mockImplementation((p: string) => p.endsWith('dotcraft.sln'))
    expect(resolveBinaryLocation(options)).toEqual({ source: 'bundled', path: null })
  })

  it('emits "crash" when process exits unexpectedly', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)

    const crash = vi.fn()
    manager.on('crash', crash)
    manager.spawn()
    proc.emit('spawn')

    proc.emit('exit', 1, null)

    expect(crash).toHaveBeenCalledOnce()
    expect(crash.mock.calls[0][0]).toMatchObject({ code: 1 })
  })

  it('closes stdin on shutdown and emits "stopped" rather than "crash"', () => {
    const { proc } = makeMockProcess()
    mockSpawn.mockReturnValue(proc)
    const stopped = vi.fn()
    const crash = vi.fn()
    manager.on('stopped', stopped)
    manager.on('crash', crash)
    manager.spawn()
    proc.emit('spawn')
    const endSpy = vi.spyOn(proc.stdin, 'end')

    manager.shutdown()
    proc.emit('exit', 0, null)

    expect(endSpy).toHaveBeenCalledOnce()
    expect(stopped).toHaveBeenCalledOnce()
    expect(crash).not.toHaveBeenCalled()
  })

  it('sends SIGTERM on shutdown when stdin is not piped (WebSocket-only mode)', () => {
    const { proc } = makeMockProcessNoStdin()
    mockSpawn.mockReturnValue(proc)
    manager = new AppServerManager({
      workspacePath: '/home/user/project',
      listenUrl: 'ws://127.0.0.1:9100'
    })

    manager.spawn()
    proc.emit('spawn')

    manager.shutdown()

    expect(proc.kill).toHaveBeenCalledWith('SIGTERM')
  })

  it('keeps polling readyz beyond the old 15s startup timeout', async () => {
    vi.useFakeTimers()
    try {
      const fetchMock = vi.fn().mockRejectedValue(new Error('not ready'))
      vi.stubGlobal('fetch', fetchMock)

      let settled = false
      const readyPromise = waitForReadyz('127.0.0.1', 9100)
      readyPromise.then(() => {
        settled = true
      })

      await vi.advanceTimersByTimeAsync(15_000)
      expect(fetchMock).toHaveBeenCalled()
      expect(settled).toBe(false)

      fetchMock.mockResolvedValue({ ok: true } as Response)
      await vi.advanceTimersByTimeAsync(APP_SERVER_READY_POLL_MS)

      await expect(readyPromise).resolves.toBe(true)
    } finally {
      vi.unstubAllGlobals()
      vi.useRealTimers()
    }
  })
})

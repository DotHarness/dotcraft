import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { requestTrayShutdown, tryAcquireTrayLock, type TrayLockHandle } from '../trayLock'

describe('trayLock', () => {
  const tempDirs: string[] = []
  const handles: TrayLockHandle[] = []

  afterEach(() => {
    for (const handle of handles.splice(0)) handle.release()
    for (const tempDir of tempDirs.splice(0)) {
      rmSync(tempDir, { recursive: true, force: true })
    }
    vi.restoreAllMocks()
  })

  function lockPath(): string {
    const tempDir = mkdtempSync(join(tmpdir(), 'dotcraft-tray-lock-'))
    tempDirs.push(tempDir)
    return join(tempDir, 'tray.lock')
  }

  function track(handle: TrayLockHandle | null): TrayLockHandle | null {
    if (handle) handles.push(handle)
    return handle
  }

  it('allows only one authenticated live tray owner', async () => {
    const path = lockPath()
    const first = track(await tryAcquireTrayLock(path))
    expect(first).not.toBeNull()

    const second = await tryAcquireTrayLock(path)
    expect(second).toBeNull()

    first?.release()
    const third = track(await tryAcquireTrayLock(path))
    expect(third).not.toBeNull()
  })

  it('recovers a legacy lock even when its pid belongs to a live process', async () => {
    const path = lockPath()
    const kill = vi.spyOn(process, 'kill')
    writeFileSync(path, JSON.stringify({ pid: process.pid, startedAt: new Date().toISOString() }), 'utf8')

    const acquired = track(await tryAcquireTrayLock(path))

    expect(acquired).not.toBeNull()
    expect(kill).not.toHaveBeenCalled()
  })

  it.each([
    ['corrupt', '{not-json'],
    ['legacy', JSON.stringify({ pid: 1234 })],
    ['unreachable', JSON.stringify({
      version: 2,
      pid: 1234,
      endpoint: { host: '127.0.0.1', port: 65_534, token: 'unreachable-token-value', protocolVersion: 1 }
    })]
  ])('recovers a %s discovery file', async (_name, contents) => {
    const path = lockPath()
    writeFileSync(path, contents, 'utf8')

    expect(track(await tryAcquireTrayLock(path))).not.toBeNull()
  })

  it('keeps exactly one owner during concurrent stale-lock recovery', async () => {
    const path = lockPath()
    writeFileSync(path, JSON.stringify({ pid: process.pid }), 'utf8')

    const results = await Promise.all(Array.from({ length: 5 }, () => tryAcquireTrayLock(path)))
    const owners = results.filter((result): result is TrayLockHandle => result !== null)
    handles.push(...owners)

    expect(owners).toHaveLength(1)
  })

  it('does not remove a replacement generation when an old owner releases', async () => {
    const path = lockPath()
    const owner = track(await tryAcquireTrayLock(path))
    expect(owner).not.toBeNull()
    const replacement = JSON.stringify({
      version: 2,
      pid: 999,
      endpoint: {
        host: '127.0.0.1',
        port: 12345,
        token: 'replacement-generation-token',
        protocolVersion: 1
      }
    })
    writeFileSync(path, replacement, 'utf8')

    owner?.release()

    expect(existsSync(path)).toBe(true)
    expect(readFileSync(path, 'utf8')).toBe(replacement)
  })

  it('accepts authenticated shutdown requests', async () => {
    const path = lockPath()
    const onShutdown = vi.fn()
    track(await tryAcquireTrayLock(path, { onShutdown }))

    await expect(requestTrayShutdown(path)).resolves.toBe(true)
    await new Promise<void>((resolve) => setImmediate(resolve))

    expect(onShutdown).toHaveBeenCalledOnce()
  })

  it('rejects shutdown when the discovery token is not owned by the server', async () => {
    const path = lockPath()
    const onShutdown = vi.fn()
    track(await tryAcquireTrayLock(path, { onShutdown }))
    const lock = JSON.parse(readFileSync(path, 'utf8')) as {
      endpoint: { token: string }
    }
    lock.endpoint.token = 'different-authentication-token'
    writeFileSync(path, JSON.stringify(lock), 'utf8')

    await expect(requestTrayShutdown(path)).resolves.toBe(false)
    await new Promise<void>((resolve) => setImmediate(resolve))

    expect(onShutdown).not.toHaveBeenCalled()
  })
})

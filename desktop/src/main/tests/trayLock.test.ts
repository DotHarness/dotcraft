import { existsSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { readTrayLockInfo, requestTrayIdentity, requestTrayShutdown, tryAcquireTrayLock, type TrayLockHandle } from '../trayLock'

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

  it('recovers corrupt, unreachable, and legacy discovery files without signalling their pid', async () => {
    const kill = vi.spyOn(process, 'kill')
    for (const contents of [
      '{not-json',
      JSON.stringify({
        version: 2,
        pid: 1234,
        endpoint: { host: '127.0.0.1', port: 65_534, token: 'unreachable-token-value', protocolVersion: 1 }
      }),
      JSON.stringify({ pid: process.pid, startedAt: new Date().toISOString() })
    ]) {
      const path = lockPath()
      writeFileSync(path, contents, 'utf8')

      expect(track(await tryAcquireTrayLock(path)), contents).not.toBeNull()
    }
    expect(kill).not.toHaveBeenCalled()
  })

  it('keeps exactly one owner during concurrent stale-lock recovery', async () => {
    const path = lockPath()
    writeFileSync(path, JSON.stringify({ pid: process.pid }), 'utf8')

    const results = await Promise.all(Array.from({ length: 3 }, () => tryAcquireTrayLock(path)))
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

  it('shuts down the captured tray even when discovery is replaced after identity verification', async () => {
    const pathA = lockPath()
    const pathB = lockPath()
    const shutdownA = vi.fn()
    const shutdownB = vi.fn()
    track(await tryAcquireTrayLock(pathA, { onShutdown: shutdownA }))
    track(await tryAcquireTrayLock(pathB, { onShutdown: shutdownB }))
    const endpointA = readTrayLockInfo(pathA)!.endpoint!

    await expect(requestTrayIdentity(endpointA)).resolves.toEqual({ pid: process.pid, executable: process.execPath })
    writeFileSync(pathA, readFileSync(pathB))
    await expect(requestTrayShutdown(endpointA)).resolves.toBe(true)
    await new Promise<void>((resolve) => setImmediate(resolve))

    expect(shutdownA).toHaveBeenCalledOnce()
    expect(shutdownB).not.toHaveBeenCalled()
  })

  it('returns the authenticated server identity without requesting shutdown', async () => {
    const path = lockPath()
    const onShutdown = vi.fn()
    track(await tryAcquireTrayLock(path, { onShutdown }))
    const identity = { pid: process.pid, executable: process.execPath }
    expect(readTrayLockInfo(path)).toMatchObject(identity)
    const endpoint = readTrayLockInfo(path)!.endpoint!
    const record = JSON.parse(readFileSync(path, 'utf8'))
    writeFileSync(path, JSON.stringify({ ...record, pid: 1234, executable: 'C:/fixtures/other.exe' }))
    await expect(requestTrayIdentity(endpoint)).resolves.toEqual(identity)
    expect(onShutdown).not.toHaveBeenCalled()
  })

  it('skips discovery records without valid installation metadata', async () => {
    const path = lockPath()
    track(await tryAcquireTrayLock(path))
    const record = JSON.parse(readFileSync(path, 'utf8'))
    for (const executable of [undefined, '', 42]) {
      writeFileSync(path, JSON.stringify({ ...record, executable }))
      expect(readTrayLockInfo(path)).toBeNull()
    }
  })

  it.each(['token', 'protocolVersion'])('rejects identity and shutdown with invalid %s', async (field) => {
    const path = lockPath()
    const onShutdown = vi.fn()
    track(await tryAcquireTrayLock(path, { onShutdown }))
    const endpoint = {
      ...readTrayLockInfo(path)!.endpoint!,
      [field]: field === 'token' ? 'different-authentication-token' : 99
    }

    await expect(requestTrayIdentity(endpoint)).resolves.toBeNull()
    await expect(requestTrayShutdown(endpoint)).resolves.toBe(false)
    await new Promise<void>((resolve) => setImmediate(resolve))

    expect(onShutdown).not.toHaveBeenCalled()
  })
})

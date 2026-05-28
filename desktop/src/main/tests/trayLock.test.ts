import { mkdtempSync, rmSync, writeFileSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { afterEach, describe, expect, it } from 'vitest'
import { tryAcquireTrayLock } from '../trayLock'

describe('trayLock', () => {
  let tempDir: string | null = null

  afterEach(() => {
    if (tempDir) {
      rmSync(tempDir, { recursive: true, force: true })
      tempDir = null
    }
  })

  function lockPath(): string {
    tempDir = mkdtempSync(join(tmpdir(), 'dotcraft-tray-lock-'))
    return join(tempDir, 'tray.lock')
  }

  it('allows only one live tray owner', () => {
    const path = lockPath()
    const first = tryAcquireTrayLock(path)
    expect(first).not.toBeNull()

    const second = tryAcquireTrayLock(path)
    expect(second).toBeNull()

    first?.release()
    const third = tryAcquireTrayLock(path)
    expect(third).not.toBeNull()
    third?.release()
  })

  it('recovers a stale tray lock', () => {
    const path = lockPath()
    writeFileSync(path, JSON.stringify({ pid: 999_999_999 }), 'utf8')

    const acquired = tryAcquireTrayLock(path)
    expect(acquired).not.toBeNull()
    acquired?.release()
  })
})

import { mkdtempSync, rmSync } from 'fs'
import { join } from 'path'
import { tmpdir } from 'os'
import { afterEach, describe, expect, it } from 'vitest'
import { acquireWorkspaceLock, checkWorkspaceLock } from '../workspaceLock'

describe('workspaceLock activation hint', () => {
  let tempDir: string | null = null

  afterEach(() => {
    if (tempDir) {
      rmSync(tempDir, { recursive: true, force: true })
      tempDir = null
    }
  })

  function workspacePath(): string {
    tempDir = mkdtempSync(join(tmpdir(), 'dotcraft-workspace-lock-'))
    return tempDir
  }

  it('does not report the Desktop activation hint as an exclusive lock', () => {
    const workspace = workspacePath()
    acquireWorkspaceLock(workspace, {
      host: '127.0.0.1',
      port: 32123,
      token: 'token',
      protocolVersion: 1
    })

    expect(checkWorkspaceLock(workspace)).toEqual({
      locked: false,
      pid: process.pid,
      activation: {
        host: '127.0.0.1',
        port: 32123,
        token: 'token',
        protocolVersion: 1
      }
    })
  })
})

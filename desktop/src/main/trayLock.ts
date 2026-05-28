import { closeSync, existsSync, mkdirSync, openSync, readFileSync, unlinkSync, writeSync } from 'fs'
import { dirname, join } from 'path'
import { homedir } from 'os'

export interface TrayLockHandle {
  path: string
  release: () => void
}

interface TrayLockInfo {
  pid?: number
  startedAt?: string
}

export function getTrayLockPath(home = homedir()): string {
  return join(home, '.craft', 'desktop', 'tray.lock')
}

export function isProcessAlive(pid: number): boolean {
  try {
    process.kill(pid, 0)
    return true
  } catch (error) {
    const code = (error as NodeJS.ErrnoException).code
    return code === 'EPERM'
  }
}

function readLockInfo(lockPath: string): TrayLockInfo | null {
  try {
    return JSON.parse(readFileSync(lockPath, 'utf8')) as TrayLockInfo
  } catch {
    return null
  }
}

export function tryAcquireTrayLock(lockPath = getTrayLockPath()): TrayLockHandle | null {
  mkdirSync(dirname(lockPath), { recursive: true })

  for (let attempt = 0; attempt < 2; attempt++) {
    try {
      const fd = openSync(lockPath, 'wx')
      const payload = JSON.stringify({
        pid: process.pid,
        startedAt: new Date().toISOString()
      }, null, 2)
      writeSync(fd, payload, 0, 'utf8')

      let released = false
      return {
        path: lockPath,
        release: () => {
          if (released) return
          released = true
          try {
            closeSync(fd)
          } catch {
            // Ignore lock cleanup failures during process shutdown.
          }
          try {
            unlinkSync(lockPath)
          } catch {
            // Ignore stale lock cleanup failures.
          }
        }
      }
    } catch (error) {
      const code = (error as NodeJS.ErrnoException).code
      if (code !== 'EEXIST') {
        throw error
      }

      const info = existsSync(lockPath) ? readLockInfo(lockPath) : null
      if (typeof info?.pid === 'number' && isProcessAlive(info.pid)) {
        return null
      }

      try {
        unlinkSync(lockPath)
      } catch {
        return null
      }
    }
  }

  return null
}

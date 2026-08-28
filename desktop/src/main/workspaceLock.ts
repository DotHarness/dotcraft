import { existsSync, mkdirSync, readFileSync, writeFileSync, unlinkSync } from 'fs'
import { join } from 'path'

export interface WorkspaceActivationEndpoint {
  host: string
  port: number
  token: string
  protocolVersion?: number
}

interface LockFileData {
  pid: number
  lockedAt: string
  activation?: WorkspaceActivationEndpoint
}

export type WorkspaceLockStatus =
  | { locked: false; pid?: number; activation?: WorkspaceActivationEndpoint }
  | { locked: true; pid: number; activation?: WorkspaceActivationEndpoint }

function getLockPath(workspacePath: string): string {
  return join(workspacePath, '.craft', 'desktop.lock')
}

function isProcessAlive(pid: number): boolean {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

/**
 * The Desktop lock is not a connection-exclusive gate, so callers must treat `locked`
 * as a non-blocking hint about a live Desktop process.
 */
function normalizeActivationEndpoint(value: unknown): WorkspaceActivationEndpoint | undefined {
  if (!value || typeof value !== 'object') return undefined
  const raw = value as Partial<WorkspaceActivationEndpoint>
  if (
    typeof raw.host === 'string' &&
    typeof raw.port === 'number' &&
    Number.isInteger(raw.port) &&
    raw.port > 0 &&
    typeof raw.token === 'string' &&
    raw.token.trim().length > 0
  ) {
    return {
      host: raw.host,
      port: raw.port,
      token: raw.token,
      protocolVersion: typeof raw.protocolVersion === 'number' ? raw.protocolVersion : undefined
    }
  }
  return undefined
}

export function checkWorkspaceLock(workspacePath: string): WorkspaceLockStatus {
  const lockPath = getLockPath(workspacePath)
  if (!existsSync(lockPath)) {
    return { locked: false }
  }
  try {
    const data = JSON.parse(readFileSync(lockPath, 'utf-8')) as LockFileData
    if (isProcessAlive(data.pid)) {
      return {
        locked: false,
        pid: data.pid,
        activation: normalizeActivationEndpoint(data.activation)
      }
    }
    // Stale lock
    return { locked: false }
  } catch {
    // Corrupt or unreadable lock file — treat as not locked
    return { locked: false }
  }
}

/**
 * Intentionally non-exclusive: AppServer multi-client support owns protocol safety, and
 * this file is only a discovery hint for tray/deep-link activation.
 */
export function acquireWorkspaceLock(
  workspacePath: string,
  activation?: WorkspaceActivationEndpoint
): { ok: true } | { ok: false; pid: number; activation?: WorkspaceActivationEndpoint } {
  const craftDir = join(workspacePath, '.craft')
  const lockPath = getLockPath(workspacePath)

  try {
    if (!existsSync(craftDir)) {
      mkdirSync(craftDir, { recursive: true })
    }
    const data: LockFileData = {
      pid: process.pid,
      lockedAt: new Date().toISOString(),
      ...(activation ? { activation } : {})
    }
    writeFileSync(lockPath, JSON.stringify(data, null, 2), 'utf-8')
  } catch {
    // If we can't write the lock (e.g. read-only FS), allow the connection anyway
    // rather than blocking the user entirely.
  }

  return { ok: true }
}

export function updateWorkspaceLockActivation(
  workspacePath: string,
  activation: WorkspaceActivationEndpoint
): void {
  const craftDir = join(workspacePath, '.craft')
  const lockPath = getLockPath(workspacePath)
  try {
    if (!existsSync(craftDir)) {
      mkdirSync(craftDir, { recursive: true })
    }

    let lockedAt = new Date().toISOString()
    if (existsSync(lockPath)) {
      try {
        const data = JSON.parse(readFileSync(lockPath, 'utf-8')) as LockFileData
        if (data.pid !== process.pid) {
          return
        }
        lockedAt = data.lockedAt || lockedAt
      } catch {
        // Rewrite corrupt self-owned locks with fresh metadata.
      }
    }

    const data: LockFileData = {
      pid: process.pid,
      lockedAt,
      activation
    }
    writeFileSync(lockPath, JSON.stringify(data, null, 2), 'utf-8')
  } catch {
    // Best-effort activation metadata; stale or missing hints must not block opens.
  }
}

/**
 * Only releases a lock owned by this process. Safe to call with an empty path or a
 * missing file; errors are swallowed because this is best-effort cleanup.
 */
export function releaseWorkspaceLock(workspacePath: string): void {
  if (!workspacePath) return
  const lockPath = getLockPath(workspacePath)
  try {
    if (!existsSync(lockPath)) return
    const data = JSON.parse(readFileSync(lockPath, 'utf-8')) as LockFileData
    if (data.pid === process.pid) {
      unlinkSync(lockPath)
    }
  } catch {
    // Best-effort — ignore errors
  }
}

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
  | { locked: false }
  | { locked: true; pid: number; activation?: WorkspaceActivationEndpoint }

function getLockPath(workspacePath: string): string {
  return join(workspacePath, '.craft', 'desktop.lock')
}

/** Returns true if the process with the given PID is currently running. */
function isProcessAlive(pid: number): boolean {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

/**
 * Reads the lock file for a workspace without writing anything.
 * Returns { locked: true, pid } if another live process holds the lock,
 * or { locked: false } if the workspace is available.
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
    if (data.pid === process.pid) {
      // This process already owns the lock
      return { locked: false }
    }
    if (isProcessAlive(data.pid)) {
      return {
        locked: true,
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
 * Attempts to acquire an exclusive lock on the workspace.
 *
 * Returns `{ ok: true }` if the lock was acquired (workspace is now owned
 * by this process).
 *
 * Returns `{ ok: false, pid }` if another live process holds the lock.
 * In that case nothing is written and the caller should report an error.
 */
export function acquireWorkspaceLock(
  workspacePath: string,
  activation?: WorkspaceActivationEndpoint
): { ok: true } | { ok: false; pid: number; activation?: WorkspaceActivationEndpoint } {
  const craftDir = join(workspacePath, '.craft')
  const lockPath = getLockPath(workspacePath)

  // Read existing lock if present
  if (existsSync(lockPath)) {
    try {
      const data = JSON.parse(readFileSync(lockPath, 'utf-8')) as LockFileData
      if (data.pid !== process.pid && isProcessAlive(data.pid)) {
        // Another live process holds the lock
        return {
          ok: false,
          pid: data.pid,
          activation: normalizeActivationEndpoint(data.activation)
        }
      }
      // Stale lock or owned by this process — fall through to overwrite
    } catch {
      // Corrupt lock — fall through to overwrite
    }
  }

  // Write (or overwrite) the lock with this process's PID
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
    // Best-effort activation metadata; the workspace lock remains the authority.
  }
}

/**
 * Releases the workspace lock if it is owned by this process.
 * Safe to call even if the workspace path is empty or the file is missing.
 * Errors are swallowed — this is best-effort cleanup.
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

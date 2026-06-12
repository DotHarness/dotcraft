import { existsSync, mkdirSync, readFileSync, unlinkSync, writeFileSync } from 'fs'
import { dirname, join } from 'path'
import { homedir } from 'os'
import type { WorkspaceActivationEndpoint } from './workspaceLock'

interface DesktopActivationLockInfo {
  pid?: number
  startedAt?: string
  activation?: WorkspaceActivationEndpoint
}

function normalizeActivationEndpoint(value: unknown): WorkspaceActivationEndpoint | null {
  if (!value || typeof value !== 'object') return null
  const raw = value as Partial<WorkspaceActivationEndpoint>
  if (
    typeof raw.host !== 'string' ||
    typeof raw.port !== 'number' ||
    !Number.isInteger(raw.port) ||
    raw.port <= 0 ||
    typeof raw.token !== 'string' ||
    raw.token.trim().length === 0
  ) {
    return null
  }
  return {
    host: raw.host,
    port: raw.port,
    token: raw.token,
    protocolVersion: typeof raw.protocolVersion === 'number' ? raw.protocolVersion : undefined
  }
}

function readLockInfo(lockPath: string): DesktopActivationLockInfo | null {
  try {
    return JSON.parse(readFileSync(lockPath, 'utf8')) as DesktopActivationLockInfo
  } catch {
    return null
  }
}

function isProcessAlive(pid: number): boolean {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

export function getDesktopActivationLockPath(home = homedir()): string {
  return join(home, '.craft', 'desktop', 'main-window.lock')
}

export function getDesktopActivationEndpoint(lockPath = getDesktopActivationLockPath()): WorkspaceActivationEndpoint | null {
  if (!existsSync(lockPath)) return null
  const info = readLockInfo(lockPath)
  if (typeof info?.pid !== 'number' || !isProcessAlive(info.pid)) return null
  return normalizeActivationEndpoint(info.activation)
}

export function updateDesktopActivationLock(
  activation: WorkspaceActivationEndpoint,
  lockPath = getDesktopActivationLockPath()
): void {
  try {
    mkdirSync(dirname(lockPath), { recursive: true })
    writeFileSync(lockPath, JSON.stringify({
      pid: process.pid,
      startedAt: new Date().toISOString(),
      activation
    }, null, 2), 'utf8')
  } catch {
    // Best-effort discovery hint for tray/menu actions.
  }
}

export function releaseDesktopActivationLock(lockPath = getDesktopActivationLockPath()): void {
  try {
    if (!existsSync(lockPath)) return
    const info = readLockInfo(lockPath)
    if (info?.pid === process.pid) {
      unlinkSync(lockPath)
    }
  } catch {
    // Best-effort cleanup; stale hints should not block launching.
  }
}

import { execFile } from 'child_process'
import { readFileSync } from 'fs'
import { homedir } from 'os'
import { join } from 'path'
import type { SharePcPeer, SharePcStatus } from '../../shared/satellites'

/**
 * Read-only view of the Satellite runtime installed on this PC: Satellite is the only
 * writer of its pairings, and no credential reference reaches the renderer.
 */

const REGISTRY_KEY = 'HKCU\\Software\\DotCraft\\Satellite'
const REGISTRY_VALUE = 'ExecutablePath'
const REGISTRY_TIMEOUT_MS = 4_000

function remoteToolHostStatePath(home: string = homedir()): string {
  return join(home, '.craft', 'remote-tool-host', 'host.json')
}

/** The executable Satellite published for itself, or null when it is not installed. */
export function readSatelliteExecutablePath(
  platform: NodeJS.Platform = process.platform
): Promise<string | null> {
  if (platform !== 'win32') return Promise.resolve(null)
  return new Promise((resolve) => {
    execFile(
      'reg',
      ['query', REGISTRY_KEY, '/v', REGISTRY_VALUE],
      { windowsHide: true, timeout: REGISTRY_TIMEOUT_MS },
      (error, stdout) => {
        if (error) {
          resolve(null)
          return
        }
        const line = stdout
          .split(/\r?\n/)
          .map((entry) => entry.trim())
          .find((entry) => entry.startsWith(REGISTRY_VALUE))
        const match = line?.match(/ExecutablePath\s+REG_(?:EXPAND_)?SZ\s+(.+)$/)
        const path = match?.[1]?.trim()
        resolve(path && path.length > 0 ? path : null)
      }
    )
  })
}

interface RawHostPeer {
  peerId?: unknown
  hubLabel?: unknown
  workspaceId?: unknown
  pairedAt?: unknown
}

interface RawHostState {
  peers?: unknown
  workspaces?: unknown
}

function text(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : undefined
}

function readHostState(path: string): RawHostState | null {
  try {
    const parsed: unknown = JSON.parse(readFileSync(path, 'utf8'))
    return parsed != null && typeof parsed === 'object' && !Array.isArray(parsed)
      ? (parsed as RawHostState)
      : null
  } catch {
    return null
  }
}

function peersOf(state: RawHostState): SharePcPeer[] {
  if (!Array.isArray(state.peers)) return []
  const folders = state.workspaces != null && typeof state.workspaces === 'object' && !Array.isArray(state.workspaces)
    ? (state.workspaces as Record<string, unknown>)
    : {}

  const result: SharePcPeer[] = []
  for (const entry of state.peers) {
    if (entry == null || typeof entry !== 'object') continue
    const raw = entry as RawHostPeer
    const peerId = text(raw.peerId)
    if (!peerId) continue
    const workspaceId = text(raw.workspaceId)
    const folderPath = workspaceId ? text(folders[workspaceId]) : undefined
    result.push({
      peerId,
      hubLabel: text(raw.hubLabel) ?? '',
      ...(folderPath ? { folderPath } : {}),
      ...(text(raw.pairedAt) ? { pairedAt: text(raw.pairedAt) as string } : {})
    })
  }
  return result
}

export async function readSharePcStatus(options?: {
  statePath?: string
  platform?: NodeJS.Platform
}): Promise<SharePcStatus> {
  const state = readHostState(options?.statePath ?? remoteToolHostStatePath())
  const peers = state ? peersOf(state) : []
  const executablePath = await readSatelliteExecutablePath(options?.platform)
  // A paired machine proves a runtime exists even where the registry cannot be read.
  return { installed: executablePath != null || peers.length > 0, peers }
}

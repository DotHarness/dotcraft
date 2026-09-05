/**
 * Shared satellite types and pure helpers, imported by the main process and the
 * renderer alike, so no Node or Electron APIs.
 */

import type { HubSatellite, HubSatelliteInvite, HubSatelliteWorkspace } from '@dotcraft/sdk/hub'

const SATELLITE_EVENT_PREFIX = 'satellite.'

export type SatelliteEventKind = 'joined' | 'online' | 'offline' | 'revoked'

const EVENT_KINDS: readonly SatelliteEventKind[] = ['joined', 'online', 'offline', 'revoked']

export type SatelliteState = 'offline' | 'ready' | 'inUse'

export interface SatelliteWorkspace {
  workspaceId: string
  path: string
  busy: boolean
  /** `self` or `other` relative to the asking client; never a raw instance id. */
  busyOwner?: string
  leaseExpiresAt?: string
}

export interface SatelliteLease {
  workspaceId: string
  owner?: string
  expiresAt?: string
}

export interface Satellite {
  peerId: string
  /** Always the `peerId`: Hub enrollment and AppServer routing name a machine alike. */
  hostId: string
  displayName: string
  userName?: string
  osName?: string
  connected: boolean
  enrolledAt?: string
  lastSeenAt?: string
  inviteId?: string
  workspaces: SatelliteWorkspace[]
  activeLease?: SatelliteLease
}

/** The Hub token is never part of this record. */
export interface SatelliteInvite {
  inviteId: string
  url: string
  expiresAt: string
  purpose?: string
  proposedFolder?: string
}

export interface SatelliteEvent {
  kind: SatelliteEventKind
  at: string
  peerId: string
  inviteId?: string
  satellite?: Satellite
}

export interface SharePcPeer {
  peerId: string
  hubLabel: string
  folderPath?: string
  pairedAt?: string
}

export interface SharePcStatus {
  installed: boolean
  peers: SharePcPeer[]
}

export interface SatelliteListResult {
  /** False when Hub has no satellite surface at all, which is a setup state. */
  supported: boolean
  satellites: Satellite[]
  error?: string
}

export interface SatelliteJoinLink {
  url: string
  /** False when no Satellite runtime is installed, so the renderer can say so. */
  forwarded: boolean
}

/** A remembered routing choice, keyed `<workspace>::<threadId>` in Desktop settings. */
export interface SatelliteThreadRoute {
  hostId: string
  workspaceId: string
  at: string
}

export interface CreatedSatelliteInvite {
  inviteId: string
  expiresAt: string
}

/** A message catalog key with its placeholder values, never rendered English. */
export interface SatelliteLabel {
  key: string
  params?: Record<string, string | number>
}

function text(value: unknown): string | undefined {
  if (typeof value !== 'string') return undefined
  const trimmed = value.trim()
  return trimmed.length > 0 ? trimmed : undefined
}

function timestamp(value: unknown): string | undefined {
  const raw = text(value)
  if (!raw) return undefined
  return Number.isFinite(Date.parse(raw)) ? raw : undefined
}

function normalizeWorkspace(value: unknown): SatelliteWorkspace | null {
  if (value == null || typeof value !== 'object') return null
  const raw = value as Partial<HubSatelliteWorkspace>
  const workspaceId = text(raw.workspaceId)
  if (!workspaceId) return null
  return {
    workspaceId,
    path: text(raw.path) ?? '',
    busy: raw.busy === true,
    ...(text(raw.busyOwner) ? { busyOwner: text(raw.busyOwner) as string } : {}),
    ...(timestamp(raw.leaseExpiresAt) ? { leaseExpiresAt: timestamp(raw.leaseExpiresAt) as string } : {})
  }
}

function activeLeaseOf(workspaces: SatelliteWorkspace[]): SatelliteLease | undefined {
  const busy = workspaces.find((workspace) => workspace.busy)
  if (!busy) return undefined
  return {
    workspaceId: busy.workspaceId,
    ...(busy.busyOwner ? { owner: busy.busyOwner } : {}),
    ...(busy.leaseExpiresAt ? { expiresAt: busy.leaseExpiresAt } : {})
  }
}

function normalizeSatellite(value: unknown): Satellite | null {
  if (value == null || typeof value !== 'object') return null
  const raw = value as Partial<HubSatellite> & { peerId?: unknown }
  const peerId = text(raw.peerId)
  if (!peerId) return null

  const workspaces = Array.isArray(raw.workspaces)
    ? raw.workspaces.map(normalizeWorkspace).filter((entry): entry is SatelliteWorkspace => entry != null)
    : []
  const lease = activeLeaseOf(workspaces)
  const displayName = text(raw.displayName) ?? text(raw.machineName) ?? peerId

  return {
    peerId,
    hostId: peerId,
    displayName,
    ...(text(raw.userName) ? { userName: text(raw.userName) as string } : {}),
    ...(text(raw.operatingSystem) ? { osName: text(raw.operatingSystem) as string } : {}),
    connected: raw.online === true,
    ...(timestamp(raw.pairedAt) ? { enrolledAt: timestamp(raw.pairedAt) as string } : {}),
    ...(timestamp(raw.lastSeenAt) ? { lastSeenAt: timestamp(raw.lastSeenAt) as string } : {}),
    workspaces,
    ...(lease ? { activeLease: lease } : {})
  }
}

export function normalizeSatellites(value: unknown): Satellite[] {
  if (!Array.isArray(value)) return []
  const seen = new Set<string>()
  const result: Satellite[] = []
  for (const entry of value) {
    const satellite = normalizeSatellite(entry)
    if (!satellite || seen.has(satellite.peerId)) continue
    seen.add(satellite.peerId)
    result.push(satellite)
  }
  return result
}

export function satelliteState(satellite: Pick<Satellite, 'connected' | 'activeLease'>): SatelliteState {
  if (!satellite.connected) return 'offline'
  return satellite.activeLease ? 'inUse' : 'ready'
}

export function normalizeSatelliteInvite(value: unknown, request?: {
  purpose?: string
  proposedFolder?: string
}): SatelliteInvite | null {
  if (value == null || typeof value !== 'object') return null
  const raw = value as Partial<HubSatelliteInvite>
  const inviteId = text(raw.inviteId)
  const url = text(raw.url)
  const expiresAt = timestamp(raw.expiresAt)
  if (!inviteId || !url || !expiresAt) return null
  const purpose = text(request?.purpose)
  // The Hub echo wins over what was asked for; it is what the invited machine sees.
  const proposedFolder = text(raw.folder) ?? text(request?.proposedFolder)
  return {
    inviteId,
    url,
    expiresAt,
    ...(purpose ? { purpose } : {}),
    ...(proposedFolder ? { proposedFolder } : {})
  }
}

export function isInviteExpired(
  invite: Pick<SatelliteInvite, 'expiresAt'>,
  now: number = Date.now()
): boolean {
  const expiresAt = Date.parse(invite.expiresAt)
  return !Number.isFinite(expiresAt) || expiresAt <= now
}

export function parseSatelliteEvent(value: unknown): SatelliteEvent | null {
  if (value == null || typeof value !== 'object') return null
  const raw = value as { kind?: unknown; at?: unknown; data?: unknown }
  const kind = text(raw.kind)
  if (!kind || !kind.startsWith(SATELLITE_EVENT_PREFIX)) return null
  const suffix = kind.slice(SATELLITE_EVENT_PREFIX.length) as SatelliteEventKind
  if (!EVENT_KINDS.includes(suffix)) return null

  const data = raw.data != null && typeof raw.data === 'object'
    ? (raw.data as { peerId?: unknown; inviteId?: unknown })
    : {}
  const peerId = text(data.peerId)
  if (!peerId) return null

  return {
    kind: suffix,
    at: timestamp(raw.at) ?? new Date().toISOString(),
    peerId,
    ...(text(data.inviteId) ? { inviteId: text(data.inviteId) as string } : {})
  }
}

const MINUTE_MS = 60_000
const HOUR_MS = 60 * MINUTE_MS
const DAY_MS = 24 * HOUR_MS

function plural(base: string, count: number): SatelliteLabel {
  return { key: `${base}.${count === 1 ? 'one' : 'other'}`, params: { count } }
}

export function lastSeenLabel(
  lastSeenAt: string | undefined | null,
  now: number = Date.now()
): SatelliteLabel {
  const base = 'settings.satellites.lastSeen'
  const seen = lastSeenAt ? Date.parse(lastSeenAt) : Number.NaN
  if (!Number.isFinite(seen)) return { key: `${base}.never` }

  const elapsed = Math.max(0, now - seen)
  if (elapsed < MINUTE_MS) return { key: `${base}.justNow` }
  if (elapsed < HOUR_MS) return plural(`${base}.minutes`, Math.floor(elapsed / MINUTE_MS))
  if (elapsed < DAY_MS) return plural(`${base}.hours`, Math.floor(elapsed / HOUR_MS))
  if (elapsed < 30 * DAY_MS) return plural(`${base}.days`, Math.floor(elapsed / DAY_MS))
  return { key: `${base}.on`, params: { date: new Date(seen).toISOString().slice(0, 10) } }
}

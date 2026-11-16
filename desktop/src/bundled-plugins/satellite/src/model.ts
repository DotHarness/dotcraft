import type { Satellite, SatelliteEvent, SatelliteInvite, SatelliteState } from './types'

export function satelliteState(satellite: Pick<Satellite, 'connected' | 'activeLease'>): SatelliteState {
  return !satellite.connected ? 'offline' : satellite.activeLease ? 'inUse' : 'ready'
}

export function withEventPresence(satellite: Satellite, event: SatelliteEvent): Satellite {
  if (event.kind === 'online') return { ...satellite, connected: true }
  if (event.kind === 'offline') return { ...satellite, connected: false, lastSeenAt: event.at }
  return satellite
}

export function isInviteExpired(invite: Pick<SatelliteInvite, 'expiresAt'>, now = Date.now()): boolean {
  const expiry = Date.parse(invite.expiresAt)
  return !Number.isFinite(expiry) || expiry <= now
}

export function lastSeenLabel(lastSeenAt: string | undefined | null, now = Date.now()): { key: string; params?: Record<string, string | number> } {
  const base = 'settings.satellites.lastSeen'
  const seen = lastSeenAt ? Date.parse(lastSeenAt) : Number.NaN
  if (!Number.isFinite(seen)) return { key: `${base}.never` }
  const elapsed = Math.max(0, now - seen)
  if (elapsed < 60_000) return { key: `${base}.justNow` }
  for (const [unit, size, ceiling] of [['minutes', 60_000, 3_600_000], ['hours', 3_600_000, 86_400_000], ['days', 86_400_000, 30 * 86_400_000]] as const) {
    if (elapsed < ceiling) {
      const count = Math.floor(elapsed / size)
      return { key: `${base}.${unit}.${count === 1 ? 'one' : 'other'}`, params: { count } }
    }
  }
  return { key: `${base}.on`, params: { date: new Date(seen).toISOString().slice(0, 10) } }
}

export function basename(path: string): string {
  return path.replace(/[\\/]+$/, '').split(/[\\/]/).pop() ?? path
}

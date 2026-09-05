import { describe, expect, it } from 'vitest'
import {
  isInviteExpired,
  lastSeenLabel,
  normalizeSatelliteInvite,
  normalizeSatellites,
  parseSatelliteEvent,
  satelliteState
} from '../satellites'

const NOW = Date.parse('2026-09-05T12:00:00.000Z')

describe('normalizeSatellites', () => {
  it('maps the Hub wire shape and mirrors the peer id as the routing host id', () => {
    const satellites = normalizeSatellites([
      {
        peerId: 'sat_1',
        displayName: 'Ann PC',
        online: true,
        machineName: 'ANN-DESK',
        operatingSystem: 'Windows 11',
        userName: 'ann',
        pairedAt: '2026-09-01T08:00:00.000Z',
        lastSeenAt: '2026-09-05T11:59:30.000Z',
        workspaces: [
          { workspaceId: 'ws-1', path: 'D:/art', busy: false },
          {
            workspaceId: 'ws-2',
            path: 'D:/shots',
            busy: true,
            busyOwner: 'other',
            leaseExpiresAt: '2026-09-05T12:01:00.000Z'
          }
        ]
      }
    ])

    expect(satellites).toHaveLength(1)
    expect(satellites[0]).toMatchObject({
      peerId: 'sat_1',
      hostId: 'sat_1',
      displayName: 'Ann PC',
      userName: 'ann',
      osName: 'Windows 11',
      connected: true,
      enrolledAt: '2026-09-01T08:00:00.000Z',
      lastSeenAt: '2026-09-05T11:59:30.000Z',
      activeLease: {
        workspaceId: 'ws-2',
        owner: 'other',
        expiresAt: '2026-09-05T12:01:00.000Z'
      }
    })
    expect(satellites[0].workspaces).toHaveLength(2)
  })

  it('falls back to the machine name and then the id for a missing display name', () => {
    const [named, unnamed] = normalizeSatellites([
      { peerId: 'sat_1', machineName: 'ANN-DESK', online: false },
      { peerId: 'sat_2', online: false }
    ])

    expect(named.displayName).toBe('ANN-DESK')
    expect(unnamed.displayName).toBe('sat_2')
  })

  it('drops unusable entries, duplicates and invalid timestamps', () => {
    const satellites = normalizeSatellites([
      { peerId: 'sat_1', online: true, lastSeenAt: 'not a date' },
      { peerId: 'sat_1', online: false },
      { peerId: '  ', online: true },
      null,
      'sat_3'
    ])

    expect(satellites.map((entry) => entry.peerId)).toEqual(['sat_1'])
    expect(satellites[0].lastSeenAt).toBeUndefined()
  })

  it('returns an empty list for a non-array payload', () => {
    expect(normalizeSatellites(undefined)).toEqual([])
    expect(normalizeSatellites({ satellites: [] })).toEqual([])
  })
})

describe('satelliteState', () => {
  it('reports offline, ready and in use', () => {
    expect(satelliteState({ connected: false })).toBe('offline')
    expect(satelliteState({ connected: true })).toBe('ready')
    expect(satelliteState({ connected: true, activeLease: { workspaceId: 'ws-1' } })).toBe('inUse')
  })

  it('reports offline even while a lease is still recorded', () => {
    expect(satelliteState({ connected: false, activeLease: { workspaceId: 'ws-1' } })).toBe('offline')
  })
})

describe('invitations', () => {
  it('keeps only the id, url and expiry plus the caller intent', () => {
    const invite = normalizeSatelliteInvite(
      {
        inviteId: 'inv_1',
        url: 'http://192.168.1.20:47600/i/inv_1',
        expiresAt: '2026-09-06T12:00:00.000Z',
        token: 'secret'
      },
      { purpose: 'Render check', proposedFolder: 'D:/shots' }
    )

    expect(invite).toEqual({
      inviteId: 'inv_1',
      url: 'http://192.168.1.20:47600/i/inv_1',
      expiresAt: '2026-09-06T12:00:00.000Z',
      purpose: 'Render check',
      proposedFolder: 'D:/shots'
    })
  })

  it('prefers the folder the Hub echoed over the one that was asked for', () => {
    const wire = {
      inviteId: 'inv_1',
      url: 'http://192.168.1.20:47600/i/inv_1',
      expiresAt: '2026-09-06T12:00:00.000Z',
      folder: 'D:/shots/approved'
    }

    expect(normalizeSatelliteInvite(wire, { proposedFolder: 'D:/shots' })?.proposedFolder)
      .toBe('D:/shots/approved')
    expect(normalizeSatelliteInvite(wire)?.proposedFolder).toBe('D:/shots/approved')
    expect(normalizeSatelliteInvite({ ...wire, folder: '  ' })?.proposedFolder).toBeUndefined()
  })

  it('rejects an invitation missing any required field', () => {
    expect(normalizeSatelliteInvite({ url: 'http://h/i', expiresAt: '2026-09-06T12:00:00.000Z' })).toBeNull()
    expect(normalizeSatelliteInvite({ inviteId: 'inv_1', url: 'http://h/i', expiresAt: 'soon' })).toBeNull()
    expect(normalizeSatelliteInvite(null)).toBeNull()
  })

  it('treats an unparsable or past expiry as expired', () => {
    expect(isInviteExpired({ expiresAt: '2026-09-06T12:00:00.000Z' }, NOW)).toBe(false)
    expect(isInviteExpired({ expiresAt: '2026-09-05T12:00:00.000Z' }, NOW)).toBe(true)
    expect(isInviteExpired({ expiresAt: 'never' }, NOW)).toBe(true)
  })
})

describe('parseSatelliteEvent', () => {
  it.each(['joined', 'online', 'offline', 'revoked'] as const)('reads a satellite.%s frame', (kind) => {
    expect(parseSatelliteEvent({
      kind: `satellite.${kind}`,
      at: '2026-09-05T12:00:00.000Z',
      data: { peerId: 'sat_1' }
    })).toEqual({ kind, at: '2026-09-05T12:00:00.000Z', peerId: 'sat_1' })
  })

  it.each([
    { kind: 'appserver.started', at: '2026-09-05T12:00:00.000Z', data: { peerId: 'sat_1' } },
    { kind: 'satellite.renamed', at: '2026-09-05T12:00:00.000Z', data: { peerId: 'sat_1' } },
    { kind: 'satellite.online', at: '2026-09-05T12:00:00.000Z', data: {} },
    { kind: 'satellite.online', at: '2026-09-05T12:00:00.000Z' },
    null
  ])('ignores frames this surface does not own: %j', (frame) => {
    expect(parseSatelliteEvent(frame)).toBeNull()
  })
})

describe('lastSeenLabel', () => {
  it('returns catalog keys and placeholders rather than English', () => {
    const base = 'settings.satellites.lastSeen'
    expect(lastSeenLabel(undefined, NOW)).toEqual({ key: `${base}.never` })
    expect(lastSeenLabel('not a date', NOW)).toEqual({ key: `${base}.never` })
    expect(lastSeenLabel('2026-09-05T11:59:31.000Z', NOW)).toEqual({ key: `${base}.justNow` })
    expect(lastSeenLabel('2026-09-05T11:59:00.000Z', NOW)).toEqual({
      key: `${base}.minutes.one`,
      params: { count: 1 }
    })
    expect(lastSeenLabel('2026-09-05T09:00:00.000Z', NOW)).toEqual({
      key: `${base}.hours.other`,
      params: { count: 3 }
    })
    expect(lastSeenLabel('2026-09-04T09:00:00.000Z', NOW)).toEqual({
      key: `${base}.days.one`,
      params: { count: 1 }
    })
    expect(lastSeenLabel('2026-01-02T09:00:00.000Z', NOW)).toEqual({
      key: `${base}.on`,
      params: { date: '2026-01-02' }
    })
  })
})

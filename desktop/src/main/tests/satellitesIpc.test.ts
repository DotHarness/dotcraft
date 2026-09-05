import { mkdtempSync, rmSync, writeFileSync, mkdirSync } from 'fs'
import { tmpdir } from 'os'
import { join } from 'path'
import { afterAll, beforeEach, describe, expect, it, vi } from 'vitest'

const home = vi.hoisted(() => ({ path: '' }))
const registry = vi.hoisted(() => ({
  execFile: vi.fn(
    (
      _command: string,
      _args: string[],
      _options: unknown,
      callback: (error: Error | null, stdout: string) => void
    ) => {
      callback(new Error('ERROR: The system was unable to find the specified registry key'), '')
    }
  )
}))

vi.mock('os', async (importOriginal) => {
  const actual = await importOriginal<typeof import('os')>()
  return { ...actual, homedir: () => home.path }
})

vi.mock('child_process', () => registry)

vi.mock('electron', () => ({
  ipcMain: { on: vi.fn(), removeAllListeners: vi.fn() }
}))

import type { SatelliteEvent, SatelliteListResult, SharePcStatus } from '../../shared/satellites'
import type { AppSettings } from '../settings'
import type { DesktopHubClient } from '../desktopHub'
import type { SatellitesHubBridge } from '../satellites/satellitesHubBridge'
import { registerSatellitesHandlers } from '../satellites/satellitesIpc'

const HUB_TOKEN = 'hub-bearer-token-never-crosses'

const tempRoot = mkdtempSync(join(tmpdir(), 'dotcraft-satellites-'))

function useHome(name: string): string {
  const path = join(tempRoot, name)
  mkdirSync(join(path, '.craft', 'remote-tool-host'), { recursive: true })
  home.path = path
  return path
}

function writeHostState(value: unknown): void {
  writeFileSync(
    join(home.path, '.craft', 'remote-tool-host', 'host.json'),
    JSON.stringify(value, null, 2),
    'utf8'
  )
}

interface Harness {
  invoke: <T>(channel: string, input?: unknown) => Promise<T>
  hub: {
    getStatus: ReturnType<typeof vi.fn>
    listSatellites: ReturnType<typeof vi.fn>
    createSatelliteInvite: ReturnType<typeof vi.fn>
    revokeSatellite: ReturnType<typeof vi.fn>
  }
  bridge: {
    remember: ReturnType<typeof vi.fn>
    recentActivity: ReturnType<typeof vi.fn>
    acquire: ReturnType<typeof vi.fn>
    release: ReturnType<typeof vi.fn>
  }
  settings: AppSettings
  updates: Array<Partial<AppSettings>>
}

function harness(): Harness {
  const handlers = new Map<string, (event: unknown, ...args: unknown[]) => unknown>()
  const hub = {
    getStatus: vi.fn(async () => ({ capabilities: { satellites: true }, token: HUB_TOKEN })),
    listSatellites: vi.fn(async () => [] as unknown[]),
    createSatelliteInvite: vi.fn(async () => ({
      inviteId: 'inv_1',
      url: 'http://192.168.1.20:47600/i/inv_1',
      expiresAt: '2999-01-01T00:00:00.000Z'
    })),
    revokeSatellite: vi.fn(async () => undefined)
  }
  const bridge = {
    remember: vi.fn(),
    recentActivity: vi.fn(() => [] as SatelliteEvent[]),
    acquire: vi.fn(),
    release: vi.fn()
  }
  const settings: AppSettings = {}
  const updates: Array<Partial<AppSettings>> = []

  registerSatellitesHandlers({
    handleSafe: (channel, listener) => handlers.set(channel, listener as never),
    getHubClient: () => hub as unknown as DesktopHubClient,
    bridge: bridge as unknown as SatellitesHubBridge,
    getSettings: () => settings,
    updateSettings: (partial) => {
      updates.push(partial)
      Object.assign(settings, partial)
    }
  })

  return {
    invoke: async <T>(channel: string, input?: unknown): Promise<T> => {
      const handler = handlers.get(channel)
      if (!handler) throw new Error(`No handler for ${channel}`)
      return await (handler({}, input) as Promise<T>)
    },
    hub,
    bridge,
    settings,
    updates
  }
}

beforeEach(() => {
  registry.execFile.mockClear()
  useHome('default')
})

afterAll(() => {
  rmSync(tempRoot, { recursive: true, force: true })
})

describe('satellites:list', () => {
  it('returns the normalized list and seeds the activity bridge', async () => {
    const app = harness()
    app.hub.listSatellites.mockResolvedValue([
      { peerId: 'sat_1', displayName: 'Ann PC', online: true, workspaces: [] }
    ])

    const result = await app.invoke<SatelliteListResult>('satellites:list')

    expect(result.supported).toBe(true)
    expect(result.satellites).toEqual([
      { peerId: 'sat_1', hostId: 'sat_1', displayName: 'Ann PC', connected: true, workspaces: [] }
    ])
    expect(app.bridge.remember).toHaveBeenCalledWith(result.satellites)
  })

  it('reports a setup state for a Hub without the satellite surface', async () => {
    const app = harness()
    app.hub.getStatus.mockResolvedValue({ capabilities: { events: true } })
    app.hub.listSatellites.mockRejectedValue(new Error('HTTP 404 Not Found'))

    expect(await app.invoke<SatelliteListResult>('satellites:list')).toEqual({
      supported: false,
      satellites: []
    })
  })

  it('reports an error, not a setup state, when a capable Hub fails to list', async () => {
    const app = harness()
    app.hub.listSatellites.mockRejectedValue(new Error('registry unreadable'))

    expect(await app.invoke<SatelliteListResult>('satellites:list')).toEqual({
      supported: true,
      satellites: [],
      error: 'registry unreadable'
    })
  })

  it('survives a Hub whose status call fails but whose listing works', async () => {
    const app = harness()
    app.hub.getStatus.mockRejectedValue(new Error('hub restarting'))
    app.hub.listSatellites.mockResolvedValue([{ peerId: 'sat_1', online: false, workspaces: [] }])

    const result = await app.invoke<SatelliteListResult>('satellites:list')
    expect(result.supported).toBe(true)
    expect(result.satellites).toHaveLength(1)
  })
})

describe('satellites:create-invite', () => {
  it('forwards the folder and returns only invitation fields, never a Hub secret', async () => {
    const app = harness()
    app.hub.createSatelliteInvite.mockResolvedValue({
      inviteId: 'inv_1',
      url: 'http://192.168.1.20:47600/i/inv_1',
      expiresAt: '2999-01-01T00:00:00.000Z',
      folder: 'D:/shots',
      token: HUB_TOKEN,
      credentialReference: 'DotCraft/RemoteToolHost/peer/sat_1'
    })

    const invite = await app.invoke<Record<string, unknown>>('satellites:create-invite', {
      name: '  Ann PC  ',
      purpose: 'Render check',
      folder: '  D:/shots  ',
      ttlHours: 12.7
    })

    expect(Object.keys(invite).sort()).toEqual([
      'expiresAt', 'inviteId', 'proposedFolder', 'purpose', 'url'
    ])
    expect(invite.proposedFolder).toBe('D:/shots')
    expect(JSON.stringify(invite)).not.toContain(HUB_TOKEN)
    expect(app.hub.createSatelliteInvite).toHaveBeenCalledWith({
      name: 'Ann PC',
      purpose: 'Render check',
      folder: 'D:/shots',
      ttlHours: 12
    })
  })

  it('omits the folder entirely when none was chosen', async () => {
    const app = harness()

    const invite = await app.invoke<Record<string, unknown>>('satellites:create-invite', {
      name: 'Ann PC',
      folder: '   '
    })

    expect(app.hub.createSatelliteInvite).toHaveBeenCalledWith({ name: 'Ann PC' })
    expect(invite.proposedFolder).toBeUndefined()
  })

  it('remembers only the invitation id and expiry, replacing any same-id entry', async () => {
    const app = harness()
    app.settings.createdSatelliteInviteIds = [
      { inviteId: 'inv_1', expiresAt: '2100-01-01T00:00:00.000Z' },
      { inviteId: 'inv_live', expiresAt: '2999-01-01T00:00:00.000Z' }
    ]

    await app.invoke('satellites:create-invite', {})

    expect(app.settings.createdSatelliteInviteIds).toEqual([
      { inviteId: 'inv_live', expiresAt: '2999-01-01T00:00:00.000Z' },
      { inviteId: 'inv_1', expiresAt: '2999-01-01T00:00:00.000Z' }
    ])
  })

  it('rejects an invitation Hub could not mint properly', async () => {
    const app = harness()
    app.hub.createSatelliteInvite.mockResolvedValue({ url: 'http://h/i' })

    await expect(app.invoke('satellites:create-invite', {})).rejects.toThrow(/unusable/i)
  })
})

describe('satellites:revoke and activity', () => {
  it('revokes by peer id and refuses an empty one', async () => {
    const app = harness()

    expect(await app.invoke('satellites:revoke', { peerId: ' sat_1 ' })).toEqual({ ok: true })
    expect(app.hub.revokeSatellite).toHaveBeenCalledWith('sat_1')

    await expect(app.invoke('satellites:revoke', { peerId: '   ' })).rejects.toThrow()
  })

  it('reads recent activity from the bridge, scoped when a machine is given', async () => {
    const app = harness()

    await app.invoke('satellites:activity', { peerId: 'sat_1' })
    await app.invoke('satellites:activity', {})

    expect(app.bridge.recentActivity).toHaveBeenNthCalledWith(1, 'sat_1')
    expect(app.bridge.recentActivity).toHaveBeenNthCalledWith(2, undefined)
  })
})

describe('satellites:share-status', () => {
  it('reports no runtime when no host state exists', async () => {
    useHome('no-state')
    const app = harness()

    expect(await app.invoke<SharePcStatus>('satellites:share-status')).toEqual({
      installed: false,
      peers: []
    })
  })

  it('reads paired machines and their folders without exposing credentials', async () => {
    useHome('with-state')
    writeHostState({
      profileVersion: '1',
      hostId: 'rth_local',
      displayName: 'Ann PC',
      peers: [
        {
          peerId: 'sat_1',
          hubHost: '192.168.1.20',
          hubPort: 47600,
          credentialReference: 'DotCraft/RemoteToolHost/peer/sat_1',
          hubLabel: "Bo's DotCraft",
          workspaceId: 'ws-1',
          pairedAt: '2026-09-01T08:00:00.000Z'
        },
        { hubLabel: 'no peer id' },
        null
      ],
      workspaces: { 'ws-1': 'D:/shots' }
    })
    const app = harness()

    const status = await app.invoke<SharePcStatus>('satellites:share-status')

    expect(status).toEqual({
      installed: true,
      peers: [
        {
          peerId: 'sat_1',
          hubLabel: "Bo's DotCraft",
          folderPath: 'D:/shots',
          pairedAt: '2026-09-01T08:00:00.000Z'
        }
      ]
    })
    expect(JSON.stringify(status)).not.toContain('credentialReference')
    expect(JSON.stringify(status)).not.toContain('DotCraft/RemoteToolHost')
  })

  it('treats a corrupt host state as no runtime', async () => {
    useHome('corrupt')
    writeFileSync(
      join(home.path, '.craft', 'remote-tool-host', 'host.json'),
      '{ not json',
      'utf8'
    )
    const app = harness()

    expect(await app.invoke<SharePcStatus>('satellites:share-status')).toEqual({
      installed: false,
      peers: []
    })
  })

  it('reports the runtime as installed from the published executable path alone', async () => {
    useHome('registry-only')
    registry.execFile.mockImplementation((_command, _args, _options, callback) => {
      callback(null, '\r\nHKEY_CURRENT_USER\\Software\\DotCraft\\Satellite\r\n    ExecutablePath    REG_SZ    C:\\Users\\ann\\AppData\\Local\\DotCraft Satellite\\current\\dotcraft-satellite.exe\r\n\r\n')
    })
    const app = harness()

    expect(await app.invoke<SharePcStatus>('satellites:share-status')).toEqual({
      installed: true,
      peers: []
    })
    expect(registry.execFile).toHaveBeenCalledWith(
      'reg',
      ['query', 'HKCU\\Software\\DotCraft\\Satellite', '/v', 'ExecutablePath'],
      expect.objectContaining({ windowsHide: true }),
      expect.any(Function)
    )
  })
})

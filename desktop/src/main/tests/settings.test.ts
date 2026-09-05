import { describe, expect, it, vi } from 'vitest'
import {
  clearRecentWorkspaces,
  normalizeCreatedSatelliteInviteIds,
  normalizePinnedProjectIds,
  normalizePinnedThreadIdsByWorkspace,
  normalizeProfileSettings,
  normalizeSatelliteRouteByThread,
  normalizeShowInMenuBar,
  removeRecentWorkspace
} from '../settings'

vi.mock('electron', () => ({
  app: {
    getLocale: () => 'en-US',
    getPath: () => 'C:\\Users\\test\\AppData\\Roaming\\DotCraft'
  }
}))

describe('settings normalization', () => {
  it('normalizes and de-duplicates pinned local and remote project ids', () => {
    expect(normalizePinnedProjectIds({
      pinnedProjectIds: [
        ' C:\\fixtures\\sample-project ',
        'c:/fixtures/sample-project/',
        ' remote:servers:host-1:stack-1 ',
        'remote:servers:host-1:stack-1',
        '',
        '   '
      ]
    })).toEqual([
      'c:/fixtures/sample-project',
      'remote:servers:host-1:stack-1'
    ])
  })

  it('drops an invalid pinned project setting', () => {
    expect(normalizePinnedProjectIds({ pinnedProjectIds: [] })).toBeUndefined()
    expect(normalizePinnedProjectIds({
      pinnedProjectIds: 'C:/fixtures/sample-project' as unknown as string[]
    })).toBeUndefined()
  })

  it('removes the matching local project pin with a recent project', () => {
    const settings = {
      recentWorkspaces: [{ path: 'C:\\fixtures\\sample-project', name: 'sample-project', lastOpenedAt: '2026-01-01' }],
      pinnedProjectIds: ['c:/fixtures/sample-project/', 'remote:servers:studio:sample-project']
    }

    removeRecentWorkspace(settings, 'C:\\fixtures\\sample-project')

    expect(settings.pinnedProjectIds).toEqual(['remote:servers:studio:sample-project'])
  })

  it('keeps remote pins when local recent projects are cleared', () => {
    const settings = {
      recentWorkspaces: [{ path: 'C:\\fixtures\\sample-project', name: 'sample-project', lastOpenedAt: '2026-01-01' }],
      pinnedProjectIds: ['C:\\fixtures\\sample-project', 'remote:servers:studio:sample-project']
    }

    clearRecentWorkspaces(settings)

    expect(settings.pinnedProjectIds).toEqual(['remote:servers:studio:sample-project'])
  })

  it('normalizes pinned thread ids by workspace path', () => {
    const normalized = normalizePinnedThreadIdsByWorkspace({
      pinnedThreadIdsByWorkspace: {
        ' E:/examples/project/../workspace ': [
          ' thread-a ',
          'thread-b',
          'thread-a',
          '',
          'bad\u0000id'
        ],
        ' remote:servers:host-1:stack-1 ': [
          ' remote-thread ',
          'remote-thread',
          'remote-thread-2'
        ],
        '   ': ['thread-c'],
        'E:/examples/empty': [],
        'E:/examples/not-array': 'thread-c' as unknown as string[]
      }
    })

    expect(normalized).toEqual({
      'e:/examples/workspace': ['thread-a', 'thread-b'],
      'remote:servers:host-1:stack-1': ['remote-thread', 'remote-thread-2']
    })
  })

  it('merges pinned thread ids from legacy path key variants', () => {
    const normalized = normalizePinnedThreadIdsByWorkspace({
      pinnedThreadIdsByWorkspace: {
        'C:\\fixtures\\sample-project': ['thread-a', 'thread-b'],
        'c:/fixtures/sample-project/': ['thread-b', 'thread-c']
      }
    })

    expect(normalized).toEqual({
      'c:/fixtures/sample-project': ['thread-a', 'thread-b', 'thread-c']
    })
  })

  it('keeps a valid trimmed github username', () => {
    expect(normalizeProfileSettings({ profile: { githubUsername: '  Octo-Cat  ' } })).toEqual({
      githubUsername: 'Octo-Cat'
    })
  })

  it('drops invalid or empty github usernames', () => {
    expect(normalizeProfileSettings({ profile: { githubUsername: '' } })).toBeUndefined()
    expect(normalizeProfileSettings({ profile: { githubUsername: '-bad' } })).toBeUndefined()
    expect(normalizeProfileSettings({ profile: { githubUsername: 'has space' } })).toBeUndefined()
    expect(normalizeProfileSettings({})).toBeUndefined()
  })

  it('keeps a valid menu bar visibility toggle', () => {
    expect(normalizeShowInMenuBar({ showInMenuBar: true })).toBe(true)
    expect(normalizeShowInMenuBar({ showInMenuBar: false })).toBe(false)
  })

  it('drops invalid menu bar visibility values', () => {
    expect(normalizeShowInMenuBar({ showInMenuBar: 'yes' as unknown as boolean })).toBeUndefined()
    expect(normalizeShowInMenuBar({})).toBeUndefined()
  })
})

describe('satellite settings normalization', () => {
  const now = Date.parse('2026-09-05T12:00:00.000Z')
  const day = 24 * 60 * 60 * 1000
  const at = (offsetDays: number): string => new Date(now - offsetDays * day).toISOString()

  it('keeps well-formed thread routes and drops malformed keys and values', () => {
    expect(normalizeSatelliteRouteByThread({
      satelliteRouteByThread: {
        'c:/ws::thread-1': { hostId: ' sat_1 ', workspaceId: ' ws-1 ', at: at(1) },
        'thread-without-workspace': { hostId: 'sat_1', workspaceId: 'ws-1', at: at(1) },
        '   ': { hostId: 'sat_1', workspaceId: 'ws-1', at: at(1) },
        'c:/ws::thread-2': { hostId: '', workspaceId: 'ws-1', at: at(1) },
        'c:/ws::thread-3': { hostId: 'sat_1', workspaceId: 'ws-1', at: 'whenever' },
        'c:/ws::thread-4': 'sat_1' as unknown as { hostId: string }
      }
    }, now)).toEqual({
      'c:/ws::thread-1': { hostId: 'sat_1', workspaceId: 'ws-1', at: at(1) }
    })
  })

  it('drops routes older than thirty days', () => {
    const normalized = normalizeSatelliteRouteByThread({
      satelliteRouteByThread: {
        'c:/ws::fresh': { hostId: 'sat_1', workspaceId: 'ws-1', at: at(29) },
        'c:/ws::stale': { hostId: 'sat_1', workspaceId: 'ws-1', at: at(31) }
      }
    }, now)

    expect(Object.keys(normalized ?? {})).toEqual(['c:/ws::fresh'])
  })

  it('caps thread routes at two hundred entries, keeping the newest choices', () => {
    const routes: Record<string, { hostId: string; workspaceId: string; at: string }> = {}
    for (let i = 0; i < 260; i++) {
      routes[`c:/ws::thread-${i}`] = {
        hostId: 'sat_1',
        workspaceId: 'ws-1',
        at: new Date(now - i * 60_000).toISOString()
      }
    }

    const normalized = normalizeSatelliteRouteByThread({ satelliteRouteByThread: routes }, now)

    expect(Object.keys(normalized ?? {})).toHaveLength(200)
    expect(normalized?.['c:/ws::thread-0']).toBeDefined()
    expect(normalized?.['c:/ws::thread-199']).toBeDefined()
    expect(normalized?.['c:/ws::thread-200']).toBeUndefined()
  })

  it('drops an empty or invalid route map', () => {
    expect(normalizeSatelliteRouteByThread({}, now)).toBeUndefined()
    expect(normalizeSatelliteRouteByThread({ satelliteRouteByThread: {} }, now)).toBeUndefined()
    expect(normalizeSatelliteRouteByThread({
      satelliteRouteByThread: [] as unknown as Record<string, never>
    }, now)).toBeUndefined()
  })

  it('drops expired, duplicate and malformed invitation ids', () => {
    expect(normalizeCreatedSatelliteInviteIds({
      createdSatelliteInviteIds: [
        { inviteId: ' inv_live ', expiresAt: at(-1) },
        { inviteId: 'inv_live', expiresAt: at(-2) },
        { inviteId: 'inv_expired', expiresAt: at(1) },
        { inviteId: '', expiresAt: at(-1) },
        { inviteId: 'inv_bad_date', expiresAt: 'soon' },
        'inv_string' as unknown as { inviteId: string; expiresAt: string }
      ]
    }, now)).toEqual([{ inviteId: 'inv_live', expiresAt: at(-1) }])
  })

  it('keeps at most twenty remembered invitations', () => {
    const invites = Array.from({ length: 26 }, (_, index) => ({
      inviteId: `inv_${index}`,
      expiresAt: at(-1)
    }))

    const normalized = normalizeCreatedSatelliteInviteIds({ createdSatelliteInviteIds: invites }, now)

    expect(normalized).toHaveLength(20)
    expect(normalized?.[0].inviteId).toBe('inv_6')
    expect(normalized?.[19].inviteId).toBe('inv_25')
  })

  it('drops an empty or invalid invitation memory', () => {
    expect(normalizeCreatedSatelliteInviteIds({}, now)).toBeUndefined()
    expect(normalizeCreatedSatelliteInviteIds({ createdSatelliteInviteIds: [] }, now)).toBeUndefined()
  })
})

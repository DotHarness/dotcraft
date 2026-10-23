import { describe, expect, it, vi } from 'vitest'
import {
  forgetLocalProject,
  normalizeComputerUseSettings,
  normalizeCreatedSatelliteInviteIds,
  normalizePinnedProjectIds,
  normalizePinnedThreadIdsByWorkspace,
  normalizeProfileSettings,
  normalizeSatelliteRouteByThread,
  normalizeTurnBookmarksByThread,
  normalizeVoiceSettings
} from '../settings'

vi.mock('electron', () => ({
  app: {
    getLocale: () => 'en-US',
    getPath: () => 'C:\\Users\\test\\AppData\\Roaming\\DotCraft'
  }
}))

describe('settings normalization', () => {
  it('keeps one always-allowed entry per app id and drops invalid entries', () => {
    expect(normalizeComputerUseSettings({
      computerUse: {
        alwaysAllowedApps: [
          { id: ' C:/Windows/notepad.exe ', displayName: ' Notepad ' },
          { id: 'c:/windows/NOTEPAD.EXE', displayName: 'Duplicate' },
          { id: 'Microsoft.WindowsCalculator_8wekyb3d8bbwe!App', displayName: '' },
          { id: '', displayName: 'Missing id' },
          'not-an-object' as never
        ]
      }
    })).toEqual({
      alwaysAllowedApps: [
        { id: 'C:/Windows/notepad.exe', displayName: 'Notepad' },
        { id: 'Microsoft.WindowsCalculator_8wekyb3d8bbwe!App', displayName: 'Microsoft.WindowsCalculator_8wekyb3d8bbwe!App' }
      ]
    })
  })

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
    expect(normalizePinnedProjectIds({ pinnedProjectIds: [] })).toBeUndefined()
    expect(normalizePinnedProjectIds({
      pinnedProjectIds: 'C:/fixtures/sample-project' as unknown as string[]
    })).toBeUndefined()
  })

  it('forgets the pin and details of a removed local project', () => {
    const settings = {
      localProjectDetails: {
        'c:/fixtures/sample-project': { name: 'Sample' },
        'c:/fixtures/other': { name: 'Other' }
      },
      pinnedProjectIds: ['c:/fixtures/sample-project/', 'remote:servers:studio:sample-project']
    }

    forgetLocalProject(settings, 'C:\\fixtures\\sample-project')

    expect(settings.pinnedProjectIds).toEqual(['remote:servers:studio:sample-project'])
    expect(settings.localProjectDetails).toEqual({ 'c:/fixtures/other': { name: 'Other' } })
  })

  it('normalizes pinned thread ids by workspace path, merging legacy key variants', () => {
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
        'E:/examples/not-array': 'thread-c' as unknown as string[],
        'C:\\fixtures\\sample-project': ['thread-a', 'thread-b'],
        'c:/fixtures/sample-project/': ['thread-b', 'thread-c']
      }
    })

    expect(normalized).toEqual({
      'e:/examples/workspace': ['thread-a', 'thread-b'],
      'c:/fixtures/sample-project': ['thread-a', 'thread-b', 'thread-c'],
      'remote:servers:host-1:stack-1': ['remote-thread', 'remote-thread-2']
    })
  })

  it('keeps turn bookmarks per thread and drops malformed keys, ids, and empty threads', () => {
    expect(normalizeTurnBookmarksByThread({
      turnBookmarksByThread: {
        'c:/ws::thread-1': [' turn-1:user-1 ', 'turn-1:user-1', '', 7 as unknown as string, 'turn-2:user-2'],
        'c:/ws::thread-2': [],
        'thread-without-workspace': ['turn-3:user-3'],
        'c:/ws::thread-4': 'turn-4:user-4' as unknown as string[]
      }
    })).toEqual({
      'c:/ws::thread-1': ['turn-1:user-1', 'turn-2:user-2']
    })
    expect(normalizeTurnBookmarksByThread({ turnBookmarksByThread: { 'c:/ws::thread-1': [] } })).toBeUndefined()
    expect(normalizeTurnBookmarksByThread({})).toBeUndefined()
  })

  it('keeps only a valid trimmed github username', () => {
    expect(normalizeProfileSettings({ profile: { githubUsername: '  Octo-Cat  ' } })).toEqual({
      githubUsername: 'Octo-Cat'
    })
    expect(normalizeProfileSettings({ profile: { githubUsername: '-bad' } })).toBeUndefined()
    expect(normalizeProfileSettings({ profile: { githubUsername: 'has space' } })).toBeUndefined()
  })

  it('keeps the ChatGPT transcription opt-out without a microphone preference', () => {
    expect(normalizeVoiceSettings({ voice: { chatGptTranscription: false } }))
      .toEqual({ chatGptTranscription: false })
    expect(normalizeVoiceSettings({ voice: { deviceId: ' mic-1 ', chatGptTranscription: true } }))
      .toEqual({ deviceId: 'mic-1' })
    expect(normalizeVoiceSettings({ voice: { deviceId: '', chatGptTranscription: true } })).toBeUndefined()
  })
})

describe('satellite settings normalization', () => {
  const now = Date.parse('2026-09-05T12:00:00.000Z')
  const day = 24 * 60 * 60 * 1000
  const at = (offsetDays: number): string => new Date(now - offsetDays * day).toISOString()

  it('keeps well-formed recent thread routes and drops malformed or month-old ones', () => {
    expect(normalizeSatelliteRouteByThread({
      satelliteRouteByThread: {
        'c:/ws::thread-1': { hostId: ' sat_1 ', workspaceId: ' ws-1 ', at: at(1) },
        'thread-without-workspace': { hostId: 'sat_1', workspaceId: 'ws-1', at: at(1) },
        '   ': { hostId: 'sat_1', workspaceId: 'ws-1', at: at(1) },
        'c:/ws::thread-2': { hostId: '', workspaceId: 'ws-1', at: at(1) },
        'c:/ws::thread-3': { hostId: 'sat_1', workspaceId: 'ws-1', at: 'whenever' },
        'c:/ws::thread-4': 'sat_1' as unknown as { hostId: string },
        'c:/ws::stale': { hostId: 'sat_1', workspaceId: 'ws-1', at: at(31) }
      }
    }, now)).toEqual({
      'c:/ws::thread-1': { hostId: 'sat_1', workspaceId: 'ws-1', at: at(1) }
    })
    expect(normalizeSatelliteRouteByThread({ satelliteRouteByThread: {} }, now)).toBeUndefined()
    expect(normalizeSatelliteRouteByThread({
      satelliteRouteByThread: [] as unknown as Record<string, never>
    }, now)).toBeUndefined()
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
    expect(normalizeCreatedSatelliteInviteIds({ createdSatelliteInviteIds: [] }, now)).toBeUndefined()

    const remembered = normalizeCreatedSatelliteInviteIds({
      createdSatelliteInviteIds: Array.from({ length: 26 }, (_, index) => ({ inviteId: `inv_${index}`, expiresAt: at(-1) }))
    }, now)
    expect(remembered?.map((invite) => invite.inviteId)).toEqual(Array.from({ length: 20 }, (_, index) => `inv_${index + 6}`))
  })
})

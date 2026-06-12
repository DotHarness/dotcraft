import { describe, expect, it, vi } from 'vitest'
import {
  normalizePinnedThreadIdsByWorkspace,
  normalizeProfileSettings,
  normalizeShowInMenuBar
} from '../settings'

vi.mock('electron', () => ({
  app: {
    getLocale: () => 'en-US',
    getPath: () => 'C:\\Users\\test\\AppData\\Roaming\\DotCraft'
  }
}))

describe('settings normalization', () => {
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
        'F:\\Git\\dotcraft': ['thread-a', 'thread-b'],
        'f:/git/dotcraft/': ['thread-b', 'thread-c']
      }
    })

    expect(normalized).toEqual({
      'f:/git/dotcraft': ['thread-a', 'thread-b', 'thread-c']
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

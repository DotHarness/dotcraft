import { describe, expect, it, vi } from 'vitest'
import { normalizePinnedThreadIdsByWorkspace, normalizeProfileSettings } from '../settings'

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
        '   ': ['thread-c'],
        'E:/examples/empty': [],
        'E:/examples/not-array': 'thread-c' as unknown as string[]
      }
    })

    expect(normalized).toEqual({
      'E:\\examples\\workspace': ['thread-a', 'thread-b']
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
})

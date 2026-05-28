import { describe, expect, it, vi } from 'vitest'
import { normalizePinnedThreadIdsByWorkspace } from '../settings'

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
        ' E:/Git/dotcraft/../dotcraft ': [
          ' thread-a ',
          'thread-b',
          'thread-a',
          '',
          'bad\u0000id'
        ],
        '   ': ['thread-c'],
        'E:/Git/empty': [],
        'E:/Git/not-array': 'thread-c' as unknown as string[]
      }
    })

    expect(normalized).toEqual({
      'E:\\Git\\dotcraft': ['thread-a', 'thread-b']
    })
  })
})

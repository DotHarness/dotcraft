/**
 * @vitest-environment jsdom
 */

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { resolveDefaultCrossChannelOrigins } from '../utils/visibleChannelsDefaults'

const appServerSendRequest = vi.fn()
const modulesList = vi.fn()
const settingsGet = vi.fn()
const settingsSet = vi.fn()

describe('resolveDefaultCrossChannelOrigins', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    settingsGet.mockResolvedValue({ visibleChannels: [] })
    settingsSet.mockResolvedValue(undefined)
    modulesList.mockResolvedValue([{ channelName: 'qq' }, { channelName: 'cron' }])
    appServerSendRequest.mockImplementation(async (method: string) => {
      if (method === 'channel/list') {
        return {
          channels: [
            { name: 'acp', category: 'builtin' },
            { name: 'external-client', category: 'external' },
            { name: 'cron', category: 'system' },
            { name: 'teams', category: 'system' }
          ]
        }
      }
      return {}
    })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        appServer: { sendRequest: appServerSendRequest },
        modules: { list: modulesList },
        settings: { get: settingsGet, set: settingsSet }
      }
    })
  })

  it('ignores legacy visibleChannels settings and returns all default origins', async () => {
    await expect(resolveDefaultCrossChannelOrigins()).resolves.toEqual([
      'acp',
      'external-client',
      'qq'
    ])

    expect(settingsGet).not.toHaveBeenCalled()
    expect(settingsSet).not.toHaveBeenCalled()
  })

  it('includes teams when Agent Teams is available without exposing other system channels', async () => {
    await expect(resolveDefaultCrossChannelOrigins({ includeTeams: true })).resolves.toEqual([
      'acp',
      'external-client',
      'teams',
      'qq'
    ])
  })
})

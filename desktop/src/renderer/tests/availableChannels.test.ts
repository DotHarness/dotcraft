import { describe, expect, it } from 'vitest'
import { defaultCrossChannelOriginsFromAvailableChannels, mergeAvailableChannels } from '../utils/availableChannels'

describe('mergeAvailableChannels', () => {
  it('adds built-in social module channels that are missing from channel/list', () => {
    const merged = mergeAvailableChannels(
      [
        { name: 'acp', category: 'builtin' }
      ],
      [
        { channelName: 'qq' },
        { channelName: 'wecom' },
        { channelName: 'weixin' },
        { channelName: 'feishu' },
        { channelName: 'telegram' },
        { channelName: 'cron' }
      ]
    )

    expect(merged).toEqual([
      { name: 'acp', category: 'builtin' },
      { name: 'qq', category: 'social' },
      { name: 'wecom', category: 'social' },
      { name: 'weixin', category: 'social' },
      { name: 'feishu', category: 'social' },
      { name: 'telegram', category: 'social' }
    ])
  })

  it('does not duplicate channels already returned by channel/list', () => {
    const merged = mergeAvailableChannels(
      [{ name: 'feishu', category: 'social' }],
      [{ channelName: 'feishu' }]
    )

    expect(merged).toEqual([{ name: 'feishu', category: 'social' }])
  })
})

describe('defaultCrossChannelOriginsFromAvailableChannels', () => {
  it('includes builtin, social, and external channels', () => {
    expect(
      defaultCrossChannelOriginsFromAvailableChannels([
        { name: 'acp', category: 'builtin' },
        { name: 'qq', category: 'social' },
        { name: 'feishu', category: 'social' },
        { name: 'feishu-adapter', category: 'external' },
        { name: 'cron', category: 'system' },
        { name: 'teams', category: 'system' }
      ])
    ).toEqual(['acp', 'qq', 'feishu', 'feishu-adapter'])
  })

  it('can include teams without exposing other system channels', () => {
    expect(
      defaultCrossChannelOriginsFromAvailableChannels(
        [
          { name: 'acp', category: 'builtin' },
          { name: 'cron', category: 'system' },
          { name: 'teams', category: 'system' },
          { name: 'heartbeat', category: 'system' }
        ],
        { includeTeams: true }
      )
    ).toEqual(['acp', 'teams'])
  })
})

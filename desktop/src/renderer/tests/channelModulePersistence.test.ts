import { describe, expect, it } from 'vitest'
import {
  getEnabledEmbeddedModuleChannelNames,
  isPersistedEmbeddedModuleChannelEnabled
} from '../../shared/channelModulePersistence'

describe('channel module persistence helpers', () => {
  it('selects only enabled embedded managed module channels, tolerating transport casing and whitespace', () => {
    const channels = [
      {
        name: 'telegram',
        enabled: true,
        transport: 'subprocess',
        builtinModule: 'channel-telegram'
      },
      {
        name: 'weixin',
        enabled: false,
        transport: 'subprocess',
        builtinModule: 'channel-weixin'
      },
      {
        name: 'custom-python',
        enabled: true,
        transport: 'subprocess',
        command: 'python'
      },
      {
        name: 'feishu',
        enabled: true,
        transport: 'managedWebsocket',
        builtinModule: 'channel-feishu'
      },
      {
        name: 'remote-feishu',
        enabled: true,
        transport: 'websocket'
      },
      {
        name: 'blank-module',
        enabled: true,
        transport: 'subprocess',
        builtinModule: '   '
      }
    ]

    expect(getEnabledEmbeddedModuleChannelNames(channels)).toEqual(['telegram', 'feishu'])
    expect(
      isPersistedEmbeddedModuleChannelEnabled({
        name: 'telegram',
        enabled: true,
        transport: ' SubProcess ',
        builtinModule: 'channel-telegram'
      })
    ).toBe(true)
    expect(
      isPersistedEmbeddedModuleChannelEnabled({
        name: 'feishu',
        enabled: true,
        transport: ' ManagedWebSocket ',
        builtinModule: 'channel-feishu'
      })
    ).toBe(true)
  })
})

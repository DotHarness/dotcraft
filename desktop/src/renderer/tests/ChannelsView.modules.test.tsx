import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ChannelsView } from '../components/channels/ChannelsView'
import { useConnectionStore } from '../stores/connectionStore'
import { useUIStore } from '../stores/uiStore'
import type { DiscoveredModule } from '../../preload/api'

const settingsGet = vi.fn()
const modulesList = vi.fn()
const modulesRescan = vi.fn()
const modulesReadConfig = vi.fn()
const modulesRunning = vi.fn()
const modulesQrStatus = vi.fn()
const appServerSendRequest = vi.fn()

function createWeComModule(): DiscoveredModule {
  return {
    moduleId: 'wecom-standard',
    channelName: 'wecom',
    displayName: 'WeCom',
    localizedDisplayName: {
      en: 'WeCom',
      'zh-Hans': '企业微信'
    },
    interface: {
      shortDescription: 'Connect DotCraft to WeCom bots and group workflows.',
      localizedShortDescription: {
        en: 'Connect DotCraft to WeCom bots and group workflows.',
        'zh-Hans': '让 DotCraft 接入企业微信机器人和群聊工作流。'
      },
      longDescription: 'Use the WeCom channel to receive enterprise chat events.',
      localizedLongDescription: {
        en: 'Use the WeCom channel to receive enterprise chat events.',
        'zh-Hans': '通过企业微信渠道接收企业会话事件。'
      },
      previewPrompt: 'Sync this WeCom thread into project memory.',
      localizedPreviewPrompt: {
        en: 'Sync this WeCom thread into project memory.',
        'zh-Hans': '把这段企业微信讨论同步到项目记忆中。'
      }
    },
    packageName: '@dotcraft/channel-wecom',
    configFileName: 'wecom.json',
    supportedTransports: ['websocket'],
    requiresInteractiveSetup: false,
    capabilitySummary: {
      hasChannelTools: true,
      hasStructuredDelivery: true
    },
    variant: 'standard',
    source: 'bundled',
    absolutePath: 'C:\\sample\\workspace\\sdk\\typescript\\packages\\channel-wecom',
    configDescriptors: [
      {
        key: 'wecom.callbackUrl',
        displayLabel: 'Callback URL',
        description: 'Endpoint for WeCom callback events.',
        localizedDisplayLabel: {
          en: 'Callback URL',
          'zh-Hans': '回调地址'
        },
        localizedDescription: {
          en: 'Endpoint for WeCom callback events.',
          'zh-Hans': '企业微信回调事件的接收地址。'
        },
        required: true,
        dataKind: 'string',
        masked: false,
        interactiveSetupOnly: false
      }
    ]
  }
}

describe('ChannelsView module channel display', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConnectionStore.getState().reset()
    useUIStore.getState().setSelectedChannelKey(null)
    settingsGet.mockResolvedValue({
      locale: 'zh-Hans',
      connectionMode: 'websocket',
      activeModuleVariants: {}
    })
    modulesList.mockResolvedValue([createWeComModule()])
    modulesRescan.mockResolvedValue([createWeComModule()])
    modulesReadConfig.mockResolvedValue({ config: {} })
    modulesRunning.mockResolvedValue({})
    modulesQrStatus.mockResolvedValue({ active: false, qrDataUrl: null })
    appServerSendRequest.mockResolvedValue({ channels: [] })

    Object.defineProperty(window, 'api', {
      configurable: true,
      value: {
        settings: {
          get: settingsGet
        },
        appServer: {
          sendRequest: appServerSendRequest
        },
        modules: {
          list: modulesList,
          rescan: modulesRescan,
          readConfig: modulesReadConfig,
          writeConfig: vi.fn().mockResolvedValue(undefined),
          running: modulesRunning,
          start: vi.fn().mockResolvedValue({ ok: true }),
          stop: vi.fn().mockResolvedValue({ ok: true }),
          setActiveVariant: vi.fn().mockResolvedValue({ ok: true }),
          getLogs: vi.fn().mockResolvedValue({ lines: [] }),
          qrStatus: modulesQrStatus,
          pickDirectory: vi.fn().mockResolvedValue(null),
          onRescanSummary: vi.fn(() => vi.fn()),
          onStatusChanged: vi.fn(() => vi.fn()),
          onQrUpdate: vi.fn(() => vi.fn())
        }
      }
    })
  })

  it('opens module detail from the install action', async () => {
    render(
      <LocaleProvider>
        <ChannelsView />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: '安装' }))

    await waitFor(() => {
      expect(modulesReadConfig).toHaveBeenCalledWith({ configFileName: 'wecom.json' })
    })
  })

  it('uses remote channel status for module cards instead of local module state', async () => {
    settingsGet.mockResolvedValue({
      locale: 'zh-Hans',
      connectionMode: 'remote',
      activeModuleVariants: {}
    })
    modulesRunning.mockResolvedValue({
      'wecom-standard': {
        processState: 'stopped',
        connected: false,
        restartCount: 0,
        lastExitCode: null
      }
    })
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: {
        channelStatus: true,
        externalChannelManagement: true
      }
    })
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'channel/status') {
        return Promise.resolve({
          channels: [
            {
              name: 'wecom',
              category: 'external',
              enabled: true,
              running: true
            }
          ]
        })
      }
      if (method === 'externalChannel/list') {
        return Promise.resolve({
          channels: [
            {
              name: 'wecom',
              enabled: true,
              transport: 'subprocess',
              builtinModule: 'channel-wecom'
            }
          ]
        })
      }
      return Promise.resolve({ channels: [] })
    })

    render(
      <LocaleProvider>
        <ChannelsView />
      </LocaleProvider>
    )

    expect(await screen.findByRole('img', { name: '已连接' })).toBeInTheDocument()
  })

  it('returns from module detail to the filtered catalog', async () => {
    render(
      <LocaleProvider>
        <ChannelsView />
      </LocaleProvider>
    )

    const search = await screen.findByPlaceholderText('搜索渠道')
    fireEvent.change(search, { target: { value: '企业' } })
    fireEvent.click(screen.getByRole('button', { name: /企业微信/ }))

    await screen.findByRole('button', { name: '管理' })
    fireEvent.click(screen.getByRole('button', { name: '渠道' }))

    expect(await screen.findByPlaceholderText('搜索渠道')).toHaveValue('企业')
    expect(screen.getByRole('button', { name: /企业微信/ })).toBeInTheDocument()
  })

  it('refreshes modules from the toolbar refresh action', async () => {
    render(
      <LocaleProvider>
        <ChannelsView />
      </LocaleProvider>
    )

    await screen.findByText('企业微信')
    fireEvent.click(screen.getByRole('button', { name: '刷新模块' }))

    await waitFor(() => {
      expect(modulesRescan).toHaveBeenCalled()
    })
  })
})

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ChannelsView } from '../components/channels/ChannelsView'
import { useConnectionStore } from '../stores/connectionStore'
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
    absolutePath: 'F:\\dotcraft\\sdk\\typescript\\packages\\channel-wecom',
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
          openFolder: vi.fn().mockResolvedValue({ ok: true }),
          pickDirectory: vi.fn().mockResolvedValue(null),
          onRescanSummary: vi.fn(() => vi.fn()),
          onStatusChanged: vi.fn(() => vi.fn()),
          onQrUpdate: vi.fn(() => vi.fn())
        }
      }
    })
  })

  it('shows WeCom as a localized module and does not render the old native group', async () => {
    render(
      <LocaleProvider>
        <ChannelsView />
      </LocaleProvider>
    )

    await waitFor(() => {
      expect(screen.getAllByText('企业微信').length).toBeGreaterThan(0)
    })

    expect(screen.getByText('让 DotCraft 在社交渠道中协作')).toBeInTheDocument()
    expect(screen.getByText('让 DotCraft 接入企业微信机器人和群聊工作流。')).toBeInTheDocument()
    expect(screen.queryByText('Native')).not.toBeInTheDocument()
    expect(screen.queryByText('启用此渠道')).not.toBeInTheDocument()
  })

  it('opens a standalone module detail page after selecting a channel', async () => {
    render(
      <LocaleProvider>
        <ChannelsView />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: /企业微信/ }))

    await waitFor(() => {
      expect(modulesReadConfig).toHaveBeenCalledWith({ configFileName: 'wecom.json' })
    })
    expect(screen.queryByPlaceholderText('搜索渠道')).not.toBeInTheDocument()
    expect(screen.getByText('通过企业微信渠道接收企业会话事件。')).toBeInTheDocument()
    expect(screen.getByText('把这段企业微信讨论同步到项目记忆中。')).toBeInTheDocument()
    expect(screen.getByText(/回调地址/)).toBeInTheDocument()
    expect(screen.getByText('@dotcraft/channel-wecom')).toBeInTheDocument()
    expect(screen.getByText('hasChannelTools, hasStructuredDelivery')).toBeInTheDocument()
    expect(screen.queryByText(/未启用/)).not.toBeInTheDocument()
    expect(await screen.findByText('启用渠道')).toBeInTheDocument()
  })

  it('shows a persisted embedded module channel as stopped instead of not configured', async () => {
    useConnectionStore.getState().setStatus({
      status: 'connected',
      capabilities: { externalChannelManagement: true }
    })
    appServerSendRequest.mockImplementation((method: string) => {
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

    fireEvent.click(await screen.findByRole('button', { name: /企业微信/ }))

    expect(await screen.findByText('已停止')).toBeInTheDocument()
    expect(screen.queryByText('未配置')).not.toBeInTheDocument()
    expect(screen.getByRole('switch', { name: '启用渠道' })).toHaveAttribute(
      'aria-checked',
      'true'
    )
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

    await screen.findByText('通过企业微信渠道接收企业会话事件。')
    fireEvent.click(screen.getByRole('button', { name: '渠道' }))

    expect(await screen.findByPlaceholderText('搜索渠道')).toHaveValue('企业')
    expect(screen.getByText('企业微信')).toBeInTheDocument()
  })

  it('refreshes modules from the more actions menu', async () => {
    render(
      <LocaleProvider>
        <ChannelsView />
      </LocaleProvider>
    )

    await screen.findByText('企业微信')
    fireEvent.click(screen.getByRole('button', { name: '更多操作' }))
    fireEvent.click(await screen.findByRole('menuitem', { name: '刷新模块' }))

    await waitFor(() => {
      expect(modulesRescan).toHaveBeenCalled()
    })
  })
})

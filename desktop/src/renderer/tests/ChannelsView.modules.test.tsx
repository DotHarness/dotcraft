import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ChannelsView } from '../components/channels/ChannelsView'
import { useConnectionStore } from '../stores/connectionStore'
import { useUIStore } from '../stores/uiStore'
import { useToastStore } from '../stores/toastStore'
import type { DiscoveredModule } from '../../preload/api'
import { installDesktopApiMock } from './desktopApiMock'

const settingsGet = vi.fn()
const modulesList = vi.fn()
const modulesRescan = vi.fn()
const modulesReadConfig = vi.fn()
const modulesConfigStatus = vi.fn()
const modulesRunning = vi.fn()
const modulesStart = vi.fn()
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
    useToastStore.setState({ toasts: [] })
    settingsGet.mockResolvedValue({
      locale: 'zh-Hans',
      connectionMode: 'websocket',
      activeModuleVariants: {}
    })
    modulesList.mockResolvedValue([createWeComModule()])
    modulesRescan.mockResolvedValue([createWeComModule()])
    modulesReadConfig.mockResolvedValue({ config: {} })
    modulesConfigStatus.mockResolvedValue({
      'wecom-standard': { exists: false, missingRequired: ['Callback URL'] }
    })
    modulesStart.mockResolvedValue({ ok: true })
    modulesRunning.mockResolvedValue({})
    modulesQrStatus.mockResolvedValue({ active: false, qrDataUrl: null })
    appServerSendRequest.mockResolvedValue({ channels: [] })

    installDesktopApiMock({
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
          configStatus: modulesConfigStatus,
          writeConfig: vi.fn().mockResolvedValue(undefined),
          running: modulesRunning,
          start: modulesStart,
          stop: vi.fn().mockResolvedValue({ ok: true }),
          setActiveVariant: vi.fn().mockResolvedValue({ ok: true }),
          getLogs: vi.fn().mockResolvedValue({ lines: [] }),
          qrStatus: modulesQrStatus,
          pickDirectory: vi.fn().mockResolvedValue(null),
          onRescanSummary: vi.fn(() => vi.fn()),
          onStatusChanged: vi.fn(() => vi.fn()),
          onQrUpdate: vi.fn(() => vi.fn())
        }
      })
  })

  it('opens the configuration form from the install action', async () => {
    render(
      <LocaleProvider>
        <ChannelsView />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: '安装' }))

    expect(await screen.findByRole('heading', { name: '管理 企业微信' })).toBeInTheDocument()
    await waitFor(() => {
      expect(modulesReadConfig).toHaveBeenCalledWith({ configFileName: 'wecom.json' })
    })
  })

  it('offers the module status instead of install once configuration is complete', async () => {
    modulesConfigStatus.mockResolvedValue({
      'wecom-standard': { exists: true, missingRequired: [] }
    })

    render(
      <LocaleProvider>
        <ChannelsView />
      </LocaleProvider>
    )

    expect(await screen.findByRole('img', { name: '已停止' })).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: '安装' })).not.toBeInTheDocument()
  })

  it('opens the configuration form when connecting without required fields', async () => {
    modulesConfigStatus.mockResolvedValue({
      'wecom-standard': { exists: true, missingRequired: [] }
    })
    modulesStart.mockResolvedValue({
      ok: false,
      error: 'Required fields missing: Callback URL',
      missingFields: ['Callback URL']
    })

    render(
      <LocaleProvider>
        <ChannelsView />
      </LocaleProvider>
    )

    fireEvent.click(await screen.findByRole('button', { name: /企业微信/ }))
    fireEvent.click(await screen.findByRole('button', { name: '连接' }))

    expect(await screen.findByRole('heading', { name: '管理 企业微信' })).toBeInTheDocument()
    await waitFor(() => {
      expect(
        useToastStore.getState().toasts.some((toast) => toast.message.includes('Callback URL'))
      ).toBe(true)
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

  it('maps a remote permanent channel failure to the crashed card state', async () => {
    settingsGet.mockResolvedValue({
      locale: 'zh-Hans',
      connectionMode: 'remote',
      activeModuleVariants: {}
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
              running: false,
              runtimeState: 'failed',
              failureCode: 'externalChannelStartFailed'
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

    expect(await screen.findByRole('img', { name: '错误' })).toBeInTheDocument()
    expect(screen.queryByRole('img', { name: '连接中' })).not.toBeInTheDocument()
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

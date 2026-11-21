import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { ChannelsView } from '../components/channels/ChannelsView'
import { PluginSurfaceTabs } from '../components/plugins/PluginBrowseSurface'
import { channelSettingOperations, groupChannelPlugins, isChannelOnlyPlugin, missingChannelSettings, type ChannelPlugin } from '../components/channels/channelPluginModel'
import type { PluginSettingsSnapshot } from '../plugins/desktopPluginSettings'
import { useConnectionStore } from '../stores/connectionStore'
import { usePluginStore } from '../stores/pluginStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

function plugin(id: string, installed: boolean): ChannelPlugin {
  return {
    id, key: `${id}@test-market`, displayName: 'Fixture channel', version: '1.2.0',
    rootPath: '/fixtures/plugins', installed, enabled: installed, installable: !installed,
    removable: installed, source: 'userglobal', marketplaceName: 'test-market',
    functions: [], skills: [], mcpServers: [], lspServers: [],
    channel: {
      moduleId: `${id}-standard`, channelName: 'wecom', displayName: 'Fixture channel',
      packageName: '@example/channel', supportedTransports: ['websocket'],
      variant: 'standard', requiresInteractiveSetup: false,
      configDescriptors: [{
        key: 'wecom.robots', displayLabel: 'Robots', description: '',
        dataKind: 'list', required: true, masked: true, interactiveSetupOnly: false
      }, {
        key: 'wecom.port', displayLabel: 'Port', description: '', defaultValue: 9000,
        dataKind: 'number', required: true, masked: false, interactiveSetupOnly: false
      }]
    }
  }
}

const config: PluginSettingsSnapshot = {
  schema: { fields: [] }, personal: {}, workspace: {}, value: { 'wecom.port': 9000 },
  writableScopes: ['workspace'], configuredSecrets: ['wecom.robots']
}
const sendRequest = vi.fn()
let currentPlugin: ChannelPlugin

function mount(): void {
  render(<LocaleProvider loadSettings={false}>
    <ChannelsView active navigation={<PluginSurfaceTabs value="channels" onChange={() => {}} />} />
  </LocaleProvider>)
}

beforeEach(() => {
  currentPlugin = plugin('channel-fixture', false)
  sendRequest.mockReset()
  sendRequest.mockImplementation(async (method: string) => {
    if (method === 'plugin/install') {
      currentPlugin = plugin(currentPlugin.id, true)
      return { outcome: 'applied', plugin: currentPlugin, snapshotRevision: 2 }
    }
    if (method === 'plugin/list') return { plugins: [currentPlugin], snapshotRevision: 2 }
    if (method === 'plugin/config/get' || method === 'plugin/config/mutate') return config
    if (method === 'channel/status') return { channels: [] }
    throw new Error(method)
  })
  installDesktopApiMock({ initialLocale: 'en', appServer: { sendRequest, sendRequestRaw: sendRequest } })
  usePluginStore.setState({
    loading: false, error: null, plugins: [currentPlugin],
    snapshotRevision: 1, completeSnapshotRevision: 1, hasCompleteCatalog: true
  })
  useConnectionStore.setState({
    status: 'connected', connectionId: null,
    capabilities: { pluginManagement: true, channelManagement: true, pluginConfiguration: true, channelStatus: true }
  })
  useUIStore.setState({ selectedChannelKey: null, pluginCatalogSurface: 'channels' })
})

describe('Channels catalog', () => {
  it('installs from the market and opens setup without starting an adapter', async () => {
    mount()
    fireEvent.click(screen.getByRole('button', { name: 'Install', exact: true }))

    expect(await screen.findByLabelText('Robots')).toBeTruthy()
    expect(sendRequest).toHaveBeenCalledWith('plugin/install', { id: currentPlugin.id, marketplaceName: 'test-market' })
    expect(sendRequest.mock.calls.some(([method]) => method === 'channel/setEnabled')).toBe(false)
  })

  it('does not project a late install into a different workspace', async () => {
    const installed = plugin(currentPlugin.id, true)
    let complete = () => {}
    sendRequest.mockReturnValue(new Promise((resolve) => {
      complete = () => resolve({ plugin: installed, outcome: 'applied', snapshotRevision: 2 })
    }))
    const install = usePluginStore.getState().installPlugin(currentPlugin.key)
    usePluginStore.getState().resetForWorkspaceChange()
    complete()
    await install
    expect(usePluginStore.getState()).toMatchObject({ plugins: [], snapshotRevision: 0 })
  })

  it('only sends the replacement for a stored structured secret, not effective defaults', async () => {
    currentPlugin = plugin(currentPlugin.id, true)
    usePluginStore.setState({ plugins: [currentPlugin] })
    useUIStore.setState({ selectedChannelKey: `channel:${currentPlugin.key}` })
    mount()
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    fireEvent.click(screen.getByRole('menuitem', { name: 'Manage' }))
    const input = await screen.findByLabelText<HTMLInputElement>('Robots')
    expect(input.value).toBe('')
    const replacement = [{ path: '/fixture', token: 'new fixture token' }]
    fireEvent.change(input, { target: { value: JSON.stringify(replacement) } })
    fireEvent.click(screen.getByRole('button', { name: 'Save' }))

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('plugin/config/mutate', {
      id: currentPlugin.id, scope: 'workspace', operations: [{ op: 'set', key: 'wecom.robots', value: replacement }]
    }))
  })

  it('stops a pending interactive connection when sign-in is cancelled', async () => {
    currentPlugin = plugin(currentPlugin.id, true)
    currentPlugin.channel.requiresInteractiveSetup = true
    usePluginStore.setState({ plugins: [currentPlugin] })
    useUIStore.setState({ selectedChannelKey: `channel:${currentPlugin.key}` })
    let finishStart = () => {}
    const start = new Promise<void>((resolve) => { finishStart = resolve })
    sendRequest.mockImplementation(async (method: string, params?: { enabled?: boolean }) => {
      if (method === 'plugin/config/get') return config
      if (method === 'channel/setEnabled' && params?.enabled) return start
      if (method === 'channel/setEnabled') return {}
      if (method === 'channel/setup/read') return { artifact: null }
      if (method === 'channel/status') return { channels: [] }
      throw new Error(method)
    })

    mount()
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('plugin/config/get', { id: currentPlugin.id }))
    fireEvent.click(screen.getByRole('button', { name: 'Connect' }))
    fireEvent.click(await screen.findByRole('button', { name: 'Cancel', exact: true }))
    finishStart()

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('channel/setEnabled', {
      name: 'wecom', pluginId: currentPlugin.id, enabled: false
    }))
  })

  it('leaves plugin configuration unavailable when the workspace host does not advertise it', () => {
    currentPlugin = plugin(currentPlugin.id, true)
    usePluginStore.setState({ plugins: [currentPlugin] })
    useConnectionStore.setState({ capabilities: { pluginManagement: true, channelManagement: true, channelStatus: true } })
    useUIStore.setState({ selectedChannelKey: `channel:${currentPlugin.key}` })
    mount()
    fireEvent.click(screen.getByRole('button', { name: 'More actions' }))
    expect(screen.queryByRole('menuitem', { name: 'Manage' })).toBeNull()
    expect(sendRequest.mock.calls.some(([method]) => method === 'plugin/config/get')).toBe(false)
  })

  it('prefers the configured implementation and recognizes a plugin with other contributions', () => {
    const first = plugin('first', true)
    const chosen = plugin('chosen', true)
    chosen.workflows = [{ name: 'also offers a workflow' }]
    const [variants] = groupChannelPlugins([first, chosen], new Map([['wecom', {
      name: 'WECOM', pluginId: chosen.id, enabled: false, running: false, runtimeState: 'stopped'
    }]]))
    expect(variants?.[0]?.key).toBe(chosen.key)
    expect(isChannelOnlyPlugin(first)).toBe(true)
    expect(isChannelOnlyPlugin(chosen)).toBe(false)
  })

  it('requires a host-accepted effective value, even if the descriptor declares a default', () => {
    const { channel } = plugin('channel-fixture', true)
    expect(missingChannelSettings(channel, config)).toEqual([])
    expect(missingChannelSettings(channel, { ...config, value: {} })).toEqual(['wecom.port'])
  })

  it('omits unchanged settings and keeps or clears stored credentials explicitly', () => {
    const workspace = { 'wecom.port': 9001 }
    const snapshot = { ...config, workspace }
    expect(channelSettingOperations(snapshot, { ...workspace })).toEqual([])
    expect(channelSettingOperations(snapshot, { ...workspace, 'wecom.robots': undefined }))
      .toEqual([{ op: 'unset', key: 'wecom.robots' }])
  })
})

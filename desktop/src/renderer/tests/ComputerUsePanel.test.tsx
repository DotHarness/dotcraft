import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import { ComputerUsePanel } from '../components/settings/panels/computerUse/ComputerUsePanel'
import { ConfirmDialogHost } from '../components/ui/ConfirmDialog'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useConnectionStore } from '../stores/connectionStore'
import { usePluginStore, type PluginEntry } from '../stores/pluginStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

type AllowedApp = { id: string; displayName: string }

const settingsGet = vi.fn()
const settingsSet = vi.fn()
const getAppIcon = vi.fn()
const sendRequest = vi.fn()

const computerPlugin: PluginEntry = {
  id: 'computer',
  displayName: 'Computer',
  version: '0.1.0',
  enabled: false,
  installed: false,
  installable: true,
  removable: false,
  source: 'builtin',
  rootPath: '',
  interface: { displayName: 'Computer', shortDescription: 'Control desktop apps on this computer' },
  functions: [],
  skills: [],
  mcpServers: [],
  lspServers: []
}

let catalog: PluginEntry[] = []
let revision = 1
let allowedApps: AllowedApp[] = []

function setCatalog(plugins: PluginEntry[]): void {
  catalog = plugins
  usePluginStore.setState({ plugins })
}

function patchPlugin(id: string, patch: Partial<PluginEntry>): PluginEntry {
  catalog = catalog.map((plugin) => plugin.id === id ? { ...plugin, ...patch } : plugin)
  return catalog.find((plugin) => plugin.id === id)!
}

function installApi(platform: 'win32' | 'darwin'): void {
  installDesktopApiMock({
    platform,
    settings: { get: settingsGet, set: settingsSet },
    appServer: { sendRequest },
    computerUse: { getAppIcon }
  })
}

function renderPanel(): void {
  render(
    <LocaleProvider>
      <ConfirmDialogHost />
      <ComputerUsePanel />
    </LocaleProvider>
  )
}

function anyAppSwitch(): Promise<HTMLElement> {
  return screen.findByRole('switch', { name: 'Let DotCraft control any app' })
}

beforeEach(() => {
  vi.clearAllMocks()
  revision = 1
  allowedApps = []
  settingsGet.mockImplementation(async () => ({ locale: 'en', computerUse: { alwaysAllowedApps: allowedApps } }))
  settingsSet.mockResolvedValue(undefined)
  getAppIcon.mockResolvedValue(null)
  sendRequest.mockImplementation(async (method: string, params?: { id?: string; enabled?: boolean }) => {
    if (method === 'plugin/list') return { plugins: catalog, diagnostics: [], snapshotRevision: revision }
    if (method === 'plugin/view') return { plugin: catalog.find((plugin) => plugin.id === params!.id), snapshotRevision: revision }
    if (method === 'plugin/install') {
      return { outcome: 'applied', plugin: patchPlugin(params!.id!, { installed: true, enabled: false }), snapshotRevision: ++revision }
    }
    if (method === 'plugin/setEnabled') {
      return { outcome: 'applied', plugin: patchPlugin(params!.id!, { enabled: params!.enabled }), snapshotRevision: ++revision }
    }
    if (method === 'skills/list') return { skills: [] }
    throw new Error(`Unexpected request: ${method}`)
  })
  installApi('win32')
  useConnectionStore.getState().reset()
  useConnectionStore.setState({ status: 'connected', capabilities: { pluginManagement: true } })
  usePluginStore.setState({ plugins: [], loading: false, error: null, snapshotRevision: 0, completeSnapshotRevision: 0 })
  setCatalog([computerPlugin])
})

describe('ComputerUsePanel Any app control', () => {
  it('installs and enables the computer plugin when it is missing', async () => {
    renderPanel()

    const toggle = await anyAppSwitch()
    expect(toggle).toHaveAttribute('aria-checked', 'false')
    fireEvent.click(toggle)

    await waitFor(() => expect(toggle).toHaveAttribute('aria-checked', 'true'))
    expect(sendRequest).toHaveBeenCalledWith('plugin/install', { id: 'computer' })
    expect(sendRequest).toHaveBeenCalledWith('plugin/setEnabled', { id: 'computer', enabled: true })
  })

  it('disables the computer plugin without uninstalling it', async () => {
    setCatalog([{ ...computerPlugin, installed: true, enabled: true }])
    renderPanel()

    const toggle = await anyAppSwitch()
    expect(toggle).toHaveAttribute('aria-checked', 'true')
    fireEvent.click(toggle)

    await waitFor(() => expect(toggle).toHaveAttribute('aria-checked', 'false'))
    expect(sendRequest).toHaveBeenCalledWith('plugin/setEnabled', { id: 'computer', enabled: false })
    expect(sendRequest).not.toHaveBeenCalledWith('plugin/remove', expect.anything())
    expect(catalog[0].installed).toBe(true)
  })

  it('opens the computer plugin details from the row without toggling it', async () => {
    renderPanel()

    fireEvent.click(await screen.findByRole('button', { name: /^Any app/ }))

    await waitFor(() => expect(usePluginStore.getState().selectedPlugin?.id).toBe('computer'))
    expect(useUIStore.getState()).toMatchObject({ activeMainView: 'skills', pluginCatalogSurface: 'plugins' })
    expect(sendRequest).not.toHaveBeenCalledWith('plugin/install', expect.anything())
  })

  it('hides Any app and always-allowed apps outside Windows', async () => {
    installApi('darwin')
    renderPanel()

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('plugin/list', expect.anything()))
    expect(screen.queryByRole('switch', { name: 'Let DotCraft control any app' })).not.toBeInTheDocument()
    expect(screen.queryByRole('heading', { name: 'Always-allowed apps' })).not.toBeInTheDocument()
    expect(getAppIcon).not.toHaveBeenCalled()
  })
})

describe('ComputerUsePanel always-allowed apps', () => {
  it('picks up apps allowed while Settings was in the background', async () => {
    renderPanel()
    await waitFor(() => expect(settingsGet).toHaveBeenCalled())

    allowedApps = [{ id: 'notepad.exe', displayName: 'Notepad' }]
    act(() => { window.dispatchEvent(new Event('focus')) })

    expect(await screen.findByRole('button', { name: 'Remove Notepad' })).toBeInTheDocument()
  })

  it('removes an app only after the removal is confirmed', async () => {
    allowedApps = [
      { id: 'notepad.exe', displayName: 'Notepad' },
      { id: 'calc.exe', displayName: 'Calculator' }
    ]
    renderPanel()

    fireEvent.click(await screen.findByRole('button', { name: 'Remove Notepad' }))
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Cancel' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument())
    expect(settingsSet).not.toHaveBeenCalled()

    fireEvent.click(screen.getByRole('button', { name: 'Remove Notepad' }))
    fireEvent.click(within(await screen.findByRole('dialog')).getByRole('button', { name: 'Remove' }))

    await waitFor(() => expect(settingsSet).toHaveBeenCalledWith({
      computerUse: { alwaysAllowedApps: [{ id: 'calc.exe', displayName: 'Calculator' }] }
    }))
    await waitFor(() => expect(screen.queryByRole('button', { name: 'Remove Notepad' })).not.toBeInTheDocument())
  })
})

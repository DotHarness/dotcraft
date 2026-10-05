import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SettingsView } from '../components/settings/SettingsView'
import { useConnectionStore } from '../stores/connectionStore'
import { useConfigStore } from '../stores/configStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()
const existing = { id: 'existing', displayName: 'Existing subscription', protocol: 'openai-responses', hasApiKey: false,
  authMethod: 'chatgptOAuth', chatGptAccountId: 'existing-account', endPoint: '' }

describe('Settings OAuth editor', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    useUIStore.setState({ activeMainView: 'settings', activeSettingsTab: 'llmService' })
    useConnectionStore.setState({ status: 'connected', capabilities: { workspaceConfigManagement: true, providerManagement: true, modelCatalogManagement: true } })
    useConfigStore.getState().reset()
    let saved = false
    sendRequest.mockImplementation(async (method) => {
      if (method === 'provider/list') return { providers: saved ? [existing, { ...existing, id: 'openai', displayName: 'OpenAI (ChatGPT)', chatGptAccountId: 'new-account' }] : [existing] }
      if (method === 'auth/openai/login') { saved = true; return { loggedIn: true, providerId: 'openai' } }
      if (method === 'model/list') return { success: true, models: [{ id: 'account-model' }] }
      if (method === 'config/read') return { config: { ProviderId: 'existing', ProviderPreferences: {} }, origins: {} }
      return {}
    })
    installDesktopApiMock({ platform: 'win32', settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set: vi.fn() },
      appServer: { sendRequest, onNotification: () => () => {}, getResolvedBinary: vi.fn().mockResolvedValue({ path: null }) },
      modules: { list: vi.fn().mockResolvedValue([]) } })
  })

  it.each([true, false])('keeps service-managed providers read-only when providers exist: %s', async (hasProviders) => {
    const original = sendRequest.getMockImplementation()!
    sendRequest.mockImplementation(async (method, ...args) => method === 'provider/list'
      ? { managedBy: 'modelService', providers: hasProviders ? [{ ...existing, managedBy: 'modelService', isAuthenticated: true }] : [] }
      : original(method, ...args))
    render(<LocaleProvider><SettingsView workspacePath="C:/test-workspace" /></LocaleProvider>)
    await waitFor(() => expect(screen.getByRole('button', { name: 'New provider' })).toBeDisabled())
    if (hasProviders) expect(screen.getByRole('button', { name: /Edit.*Existing subscription/i })).toBeDisabled()
  })

  it('replaces creation with the saved editor and retains an existing OAuth selection', async () => {
    render(<LocaleProvider><SettingsView workspacePath="C:/test-workspace" /></LocaleProvider>)
    fireEvent.click(await screen.findByRole('button', { name: 'New provider' }))
    fireEvent.click(screen.getByText('Sign in with ChatGPT').closest('button')!)
    await waitFor(() => expect(screen.getAllByText('Sign in with ChatGPT')).toHaveLength(2))
    fireEvent.click(screen.getAllByText('Sign in with ChatGPT').at(-1)!.closest('button')!)
    expect(await screen.findByRole('button', { name: 'Update provider' })).toBeInTheDocument()
    await waitFor(() => expect(screen.getByLabelText('Provider id')).toHaveValue('openai'))
    expect(await screen.findByRole('button', { name: /Sign out/ })).toBeInTheDocument()
    expect(sendRequest).not.toHaveBeenCalledWith('config/value/write', expect.objectContaining({ keyPath: 'ProviderId' }), expect.anything())
    expect(sendRequest).not.toHaveBeenCalledWith('config/batchWrite', expect.anything(), expect.anything())
    expect(sendRequest).not.toHaveBeenCalledWith('provider/create', expect.anything(), expect.anything())
  })
})

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SettingsView } from '../components/settings/SettingsView'
import { useConnectionStore } from '../stores/connectionStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

const sendRequest = vi.fn()
const existing = { id: 'existing', displayName: 'Existing subscription', protocol: 'openai-responses', hasApiKey: false,
  authMethod: 'chatgptOAuth', chatGptAccountId: 'existing-account', endPoint: '', isImplicit: false }

describe('Settings OAuth editor', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    useUIStore.setState({ activeMainView: 'settings', activeSettingsTab: 'llmService' })
    useConnectionStore.setState({ status: 'connected', capabilities: { workspaceConfigManagement: true, providerManagement: true, modelCatalogManagement: true } })
    let saved = false
    sendRequest.mockImplementation(async (method) => {
      if (method === 'provider/list') return { providers: saved ? [existing, { ...existing, id: 'openai', displayName: 'OpenAI (ChatGPT)', chatGptAccountId: 'new-account' }] : [existing] }
      if (method === 'auth/openai/login') { saved = true; return { loggedIn: true, providerId: 'openai' } }
      if (method === 'model/list') return { success: true, models: [{ id: 'account-model' }] }
      return {}
    })
    installDesktopApiMock({ platform: 'win32', settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set: vi.fn() },
      workspaceConfig: { getCore: vi.fn().mockResolvedValue({ workspace: { providerId: 'existing', providerPreferences: {} }, userDefaults: { providerPreferences: {} } }) },
      appServer: { sendRequest, onNotification: () => () => {}, getResolvedBinary: vi.fn().mockResolvedValue({ path: null }) },
      modules: { list: vi.fn().mockResolvedValue([]) } })
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
    expect(sendRequest).not.toHaveBeenCalledWith('workspace/config/update', expect.objectContaining({ providerId: 'openai' }), expect.anything())
    expect(sendRequest).not.toHaveBeenCalledWith('provider/create', expect.anything(), expect.anything())
  })
})

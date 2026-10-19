import { act } from 'react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SettingsView } from '../components/settings/SettingsView'
import { useConnectionStore } from '../stores/connectionStore'
import { useConfigStore } from '../stores/configStore'
import { useUIStore } from '../stores/uiStore'
import { installDesktopApiMock } from './desktopApiMock'

type Notify = (payload: { method: string; params: unknown }) => void

const sendRequest = vi.fn()
let notify: Notify | null = null
const existing = { id: 'existing', displayName: 'Existing subscription', protocol: 'openai-responses', hasApiKey: false,
  authMethod: 'chatgptOAuth', isAuthenticated: true, endPoint: '' }

describe('Settings provider sign-in', () => {
  beforeEach(() => {
    vi.resetAllMocks()
    notify = null
    useUIStore.setState({ activeMainView: 'settings', activeSettingsTab: 'llmService' })
    useConnectionStore.setState({ status: 'connected', capabilities: { workspaceConfigManagement: true, providerManagement: true, modelCatalogManagement: true } })
    useConfigStore.getState().reset()
    let saved = false
    sendRequest.mockImplementation(async (method, params) => {
      if (method === 'provider/list') {
        return { providers: saved ? [existing, { ...existing, id: 'chatgpt', displayName: 'ChatGPT', isAuthenticated: false }] : [existing] }
      }
      if (method === 'config/read' && params?.includeLayers) {
        return { config: {}, origins: {}, layers: [{ name: { type: 'user' }, version: 'user-v1', config: { Providers: { existing: {} } } }] }
      }
      if (method === 'config/batchWrite') { saved = true; return {} }
      if (method === 'provider/auth/login') return { loginId: 'login-1', providerId: 'chatgpt', revision: 1, status: 'running' }
      if (method === 'provider/auth/state') return { loginId: 'login-1', providerId: 'chatgpt', revision: 1, status: 'running' }
      if (method === 'provider/auth/status') return { isAuthenticated: false }
      if (method === 'model/list') return { success: true, models: [{ id: 'account-model' }] }
      if (method === 'config/read') return { config: { ProviderId: 'existing', ProviderPreferences: {} }, origins: {} }
      return {}
    })
    installDesktopApiMock({ platform: 'win32', settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }), set: vi.fn() },
      shell: { openExternal: vi.fn().mockResolvedValue(undefined) },
      appServer: {
        sendRequest,
        onNotification: (callback: Notify) => {
          notify = callback
          return () => undefined
        },
        getResolvedBinary: vi.fn().mockResolvedValue({ path: null })
      },
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

  it('saves a new provider before signing in and keeps a usable selection', async () => {
    render(<LocaleProvider><SettingsView workspacePath="C:/test-workspace" /></LocaleProvider>)
    fireEvent.click(await screen.findByRole('button', { name: 'New provider' }))
    fireEvent.click(screen.getByRole('combobox', { name: 'Connection type' }))
    fireEvent.click(await screen.findByRole('option', { name: /ChatGPT subscription/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('provider/auth/login', { providerId: 'chatgpt', method: 'chatgptOAuth' }, expect.anything()))
    expect(sendRequest).toHaveBeenCalledWith('config/batchWrite', {
      edits: [{
        keyPath: 'Providers.chatgpt',
        value: expect.objectContaining({ Protocol: 'openai-responses', AuthMethod: 'chatgptOAuth' }),
        mergeStrategy: 'replace'
      }],
      expectedVersion: 'user-v1'
    }, expect.anything())
    const loginIndex = sendRequest.mock.calls.findIndex(([method]) => method === 'provider/auth/login')
    const writeIndex = sendRequest.mock.calls.findIndex(([method]) => method === 'config/batchWrite')
    expect(writeIndex).toBeLessThan(loginIndex)
    expect(await screen.findByRole('button', { name: 'Update provider' })).toBeInTheDocument()
    expect(screen.getByLabelText('Provider id')).toHaveValue('chatgpt')

    act(() => notify?.({ method: 'provider/auth/updated', params: {
      loginId: 'login-1', providerId: 'chatgpt', revision: 2, status: 'completed', authentication: { isAuthenticated: true }
    } }))

    await waitFor(() => expect(sendRequest.mock.calls.filter(([method]) => method === 'provider/list').length).toBeGreaterThan(2))
    expect(sendRequest).not.toHaveBeenCalledWith('config/batchWrite', expect.objectContaining({
      edits: expect.arrayContaining([expect.objectContaining({ keyPath: 'ProviderId' })])
    }), expect.anything())
    expect(sendRequest).not.toHaveBeenCalledWith('config/value/write', expect.objectContaining({ keyPath: 'ProviderId' }), expect.anything())
  })

  it('activates a provider that signs in over an unusable selection', async () => {
    const original = sendRequest.getMockImplementation()!
    sendRequest.mockImplementation(async (method, ...args) => {
      const result = await original(method, ...args)
      if (method !== 'provider/list') return result
      return { providers: (result as { providers: Array<typeof existing> }).providers.map((provider) =>
        provider.id === 'existing' ? { ...provider, isAuthenticated: false } : provider) }
    })
    render(<LocaleProvider><SettingsView workspacePath="C:/test-workspace" /></LocaleProvider>)
    fireEvent.click(await screen.findByRole('button', { name: 'New provider' }))
    fireEvent.click(screen.getByRole('combobox', { name: 'Connection type' }))
    fireEvent.click(await screen.findByRole('option', { name: /ChatGPT subscription/ }))
    fireEvent.click(await screen.findByRole('button', { name: 'Sign in' }))
    await waitFor(() => expect(sendRequest).toHaveBeenCalledWith('provider/auth/state', { loginId: 'login-1' }, expect.anything()))

    act(() => notify?.({ method: 'provider/auth/updated', params: {
      loginId: 'login-1', providerId: 'chatgpt', revision: 2, status: 'completed', authentication: { isAuthenticated: true }
    } }))

    await waitFor(() => expect(sendRequest.mock.calls.some(([method, params]) =>
      (method === 'config/batchWrite' && JSON.stringify(params).includes('"keyPath":"ProviderId","value":"chatgpt"')) ||
      (method === 'config/value/write' && params?.keyPath === 'ProviderId' && params?.value === 'chatgpt'))).toBe(true))
  })
})

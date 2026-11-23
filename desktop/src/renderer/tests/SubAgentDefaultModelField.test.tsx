import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { SubAgentDefaultModelField } from '../components/settings/panels/subAgentModels/SubAgentDefaultModelField'
import { useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import type { WorkspaceResourceScope } from '../stores/workspaceResourceScope'

const sendRequest = vi.fn()
const sendConnectionRequest = vi.fn()
const scope: WorkspaceResourceScope = { projectKey: '/workspace', connectionKey: '/workspace', connectionId: 'connection-1' }

const providers = [
  { id: 'openai', displayName: 'OpenAI', backend: 'openai', configured: true, isConfigured: true, hasApiKey: true, authMode: 'apiKey' },
  { id: 'anthropic', displayName: 'Anthropic', backend: 'anthropic', configured: true, isConfigured: true, hasApiKey: true, authMode: 'apiKey' }
] as never[]

const mainPreference = {
  model: 'gpt-4o',
  reasoning: { enabled: false, effort: 'medium' as const, output: 'full' as const },
  speed: 'standard' as const
}

beforeEach(() => {
  const serverConfig = {
    ProviderId: 'openai',
    SubAgent: {
      ProviderPreferences: {
        anthropic: { Model: 'claude-haiku-4-5', Reasoning: { Enabled: false, Effort: 'medium', Output: 'full' }, Speed: 'standard' }
      }
    }
  }
  sendRequest.mockReset().mockImplementation(async (method: string, params?: Record<string, unknown>) => {
    if (method === 'config/read') return { config: structuredClone(serverConfig), origins: {} }
    if (method === 'config/value/write' && typeof params?.keyPath === 'string' && params.keyPath.startsWith('SubAgent.ProviderPreferences.')) {
      const providerId = params.keyPath.slice('SubAgent.ProviderPreferences.'.length)
      const prefs = serverConfig.SubAgent.ProviderPreferences as Record<string, unknown>
      if (params.value == null) delete prefs[providerId]
      else prefs[providerId] = params.value
    }
    if (method === 'model/list') return { success: true, providerId: 'openai', models: [{ id: 'gpt-4o', displayName: 'GPT-4o' }] }
    return { status: 'ok', version: 'sha256:1', filePath: '/user/.craft/config.json' }
  })
  sendConnectionRequest.mockReset().mockImplementation((_key, method, params, timeout) =>
    sendRequest(method, params, ...(timeout == null ? [] : [timeout])))
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      appServer: { sendRequest, sendConnectionRequest },
      settings: { get: async () => ({ locale: 'en' }), set: async () => {} }
    }
  })
  useConfigStore.getState().reset()
  useConfigStore.getState().activate(scope)
  useModelCatalogStore.getState().reset()
  useConnectionStore.setState({
    status: 'connected',
    connectionKey: scope.connectionKey,
    connectionId: scope.connectionId,
    capabilities: { workspaceConfigManagement: true, modelCatalogManagement: true }
  })
})

describe('SubAgentDefaultModelField', () => {
  it('writes and removes only the selected provider entry without copying other providers', async () => {
    render(
      <LocaleProvider>
        <SubAgentDefaultModelField
          mainProviderId="openai"
          mainPreference={mainPreference}
          providers={providers}
        />
      </LocaleProvider>
    )

    await waitFor(() => expect(useConfigStore.getState().status).toBe('ready'))

    fireEvent.click(screen.getByRole('button', { name: 'Custom' }))
    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith(
        'config/value/write',
        expect.objectContaining({
          keyPath: 'SubAgent.ProviderPreferences.openai',
          value: {
            Model: 'gpt-4o',
            Reasoning: { Enabled: false, Effort: 'medium', Output: 'full' },
            Speed: 'standard'
          }
        }),
        20_000
      )
    })

    fireEvent.click(screen.getByRole('button', { name: 'Inherit' }))
    await waitFor(() => {
      expect(sendRequest).toHaveBeenCalledWith(
        'config/value/write',
        expect.objectContaining({
          keyPath: 'SubAgent.ProviderPreferences.openai',
          value: null
        }),
        20_000
      )
    })

    const writes = sendRequest.mock.calls.filter(([method]) => method === 'config/value/write' || method === 'config/batchWrite')
    expect(writes).toHaveLength(2)
  })
})

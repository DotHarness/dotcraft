import { useRef } from 'react'
import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { LocaleProvider } from '../contexts/LocaleContext'
import { WelcomeModelPicker } from '../components/conversation/WelcomeResourceControls'
import { useWelcomeModelControls } from '../components/conversation/useWelcomeModelControls'
import { useConfig, useConfigStore } from '../stores/configStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useModelCatalogStore } from '../stores/modelCatalogStore'
import { useProvidersStore } from '../stores/providersStore'
import { installDesktopApiMock } from './desktopApiMock'
import { activateTestWorkspace, testWorkspaceScope } from './workspaceResourceFixtures'

const providerList = vi.fn()
const modelList = vi.fn()
const sendConnectionRequest = vi.fn()
const config = {
  ProviderId: 'openai',
  ProviderPreferences: { openai: {
    model: 'cached-model', reasoning: { enabled: false, effort: 'medium', output: 'full' }, speed: 'standard'
  } }
}
const providers = [{ id: 'openai', displayName: 'OpenAI', protocol: 'openai', authMethod: 'apiKey' }]

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((done) => { resolve = done })
  return { promise, resolve }
}

function Picker(): JSX.Element {
  const workspaceConfig = useConfig()
  const initialDraftRef = useRef(null)
  const controls = useWelcomeModelControls({ workspaceConfig, initialDraftRef })
  return <WelcomeModelPicker controls={controls} />
}

beforeEach(() => {
  vi.resetAllMocks()
  providerList.mockResolvedValue({ providers })
  modelList.mockResolvedValue({ success: true, providerId: 'openai', models: [{ id: 'cached-model' }] })
  sendConnectionRequest.mockImplementation((_key, method, params) => {
    if (method === 'config/read') return Promise.resolve({ config })
    if (method === 'provider/list') return providerList()
    if (method === 'model/list') return modelList(params)
    throw new Error(`Unexpected RPC: ${method}`)
  })
  installDesktopApiMock({ initialLocale: 'en', appServer: { sendConnectionRequest } })
  useConnectionStore.setState({ status: 'connected', capabilities: {
    workspaceConfigManagement: true, modelCatalogManagement: true, providerManagement: true
  } })
  activateTestWorkspace()
})

describe('Welcome model resource retry', () => {
  it('retries a provider-list failure without refreshing or disabling the cached model catalog', async () => {
    await useConfigStore.getState().ensureLoaded()
    await useProvidersStore.getState().reload()
    await useModelCatalogStore.getState().loadIfNeeded(false, 'openai')
    render(<LocaleProvider loadSettings={false}><Picker /></LocaleProvider>)

    providerList.mockRejectedValueOnce(new Error('provider endpoint unavailable'))
    await act(async () => { await useProvidersStore.getState().reload() })
    expect(useProvidersStore.getState().error).toBe('provider endpoint unavailable')
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))

    const retry = deferred<{ providers: typeof providers }>()
    providerList.mockReturnValueOnce(retry.promise)
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }))
    expect(sendConnectionRequest).toHaveBeenLastCalledWith(
      testWorkspaceScope.connectionKey, 'provider/list', {}, 15_000,
      { connectionId: testWorkspaceScope.connectionId }
    )
    expect(providerList).toHaveBeenCalledTimes(3)
    expect(useProvidersStore.getState()).toMatchObject({ status: 'ready' })
    expect(modelList).toHaveBeenCalledExactlyOnceWith({ providerId: 'openai' })
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))
    fireEvent.click(screen.getByRole('button', { name: 'Select model' }))

    await act(async () => { retry.resolve({ providers }) })
    await waitFor(() => expect(useProvidersStore.getState()).toMatchObject({
      status: 'ready', error: null
    }))
    expect(modelList).toHaveBeenCalledTimes(1)
  })
})

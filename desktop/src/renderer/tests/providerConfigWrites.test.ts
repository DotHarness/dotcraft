import { afterEach, describe, expect, it, vi } from 'vitest'
import { createProvider, ProviderExistsError, providerCreateValue, providerUpdateValue, type ProviderFields } from '../components/settings/providerConfigWrites'
import type { ProviderInfoWire } from '../components/settings/providerInfo'

const stored: ProviderInfoWire = {
  id: 'work',
  displayName: 'Work',
  protocol: 'openai-responses',
  hasApiKey: true,
  endPoint: 'https://api.example.com/v1',
  networkTimeoutSeconds: 60,
  authMethod: 'apiKey'
}

const unchanged: ProviderFields = {
  displayName: 'Work',
  protocol: 'openai-responses',
  backendId: null,
  authMethod: 'apiKey',
  apiKey: null,
  endPoint: 'https://api.example.com/v1',
  networkTimeoutSeconds: 60,
  supportsImageGeneration: null
}

describe('provider update edits', () => {
  it('writes nothing for an untouched provider and leaves the stored key alone', () => {
    expect(providerUpdateValue(stored, unchanged)).toEqual({})
  })

  it('writes a new key and clears emptied fields', () => {
    expect(providerUpdateValue(stored, { ...unchanged, apiKey: 'sk-new', networkTimeoutSeconds: null }))
      .toEqual({ ApiKey: 'sk-new', NetworkTimeoutSeconds: null })
  })

  it('clears the stored key when the provider switches to subscription sign-in', () => {
    expect(providerUpdateValue(stored, { ...unchanged, authMethod: 'chatgptOAuth', apiKey: 'ignored' }))
      .toEqual({ AuthMethod: 'chatgptOAuth', ApiKey: null })
  })

  it('moves a provider onto a backend without keeping its key or endpoint', () => {
    expect(providerUpdateValue(stored, {
      ...unchanged, backendId: 'local-llm', protocol: 'local-chat', authMethod: 'device', apiKey: 'ignored'
    })).toEqual({
      BackendId: 'local-llm', Protocol: 'local-chat', AuthMethod: 'device', EndPoint: null, ApiKey: null
    })
  })
})

describe('provider create value', () => {
  it('writes the backend, its protocol and sign-in method without HTTP fields', () => {
    expect(providerCreateValue({
      ...unchanged,
      displayName: '',
      backendId: 'local-llm',
      protocol: 'local-chat',
      authMethod: 'device',
      apiKey: 'ignored',
      endPoint: 'https://ignored.example.com',
      networkTimeoutSeconds: null
    })).toEqual({ BackendId: 'local-llm', Protocol: 'local-chat', AuthMethod: 'device' })
  })

  it('writes the subscription method instead of a key', () => {
    expect(providerCreateValue({ ...unchanged, authMethod: 'chatgptOAuth', apiKey: 'ignored', networkTimeoutSeconds: null }))
      .toEqual({ Protocol: 'openai-responses', DisplayName: 'Work', AuthMethod: 'chatgptOAuth', EndPoint: 'https://api.example.com/v1' })
  })
})

describe('provider create', () => {
  afterEach(() => {
    vi.unstubAllGlobals()
  })

  function stubConfigRead(userConfig: Record<string, unknown>) {
    const sendRequest = vi.fn(async (method: string) => method === 'config/read'
      ? { layers: [{ name: { type: 'user' }, version: 'v1', config: userConfig }] }
      : {})
    vi.stubGlobal('window', { api: { appServer: { sendRequest } } })
    return sendRequest
  }

  it('refuses an id that the fresh configuration already holds', async () => {
    const sendRequest = stubConfigRead({ Providers: { WORK: { Protocol: 'openai-responses' } } })
    await expect(createProvider('work', unchanged)).rejects.toBeInstanceOf(ProviderExistsError)
    expect(sendRequest).not.toHaveBeenCalledWith('config/batchWrite', expect.anything(), expect.anything())
  })

  it('writes against the version of the snapshot it checked', async () => {
    const sendRequest = stubConfigRead({ Providers: { other: { Protocol: 'openai-responses' } } })
    await createProvider('work', unchanged)
    expect(sendRequest).toHaveBeenCalledWith(
      'config/batchWrite',
      expect.objectContaining({ expectedVersion: 'v1' }),
      expect.anything()
    )
  })
})

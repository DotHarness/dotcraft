import { describe, expect, it } from 'vitest'
import { providerUpdateValue, type ProviderFields } from '../components/settings/providerConfigWrites'
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
})

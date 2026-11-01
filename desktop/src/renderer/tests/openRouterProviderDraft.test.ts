import { describe, expect, it } from 'vitest'
import { translate } from '../../shared/locales'
import { applyProviderConnection, createProviderDraft, providerConnectionMode, providerConnectionOptions } from '../components/settings/providers/providerDraft'
import type { ProviderBackendInfo } from '@dotcraft/sdk/contracts'

const backend: ProviderBackendInfo = {
  id: 'openrouter', displayName: 'OpenRouter', defaultProtocol: 'auto',
  protocols: ['auto', 'anthropic', 'openai-responses', 'openai-chat-completions'],
  authenticationMethods: [
    { id: 'apiKey', displayName: 'API key', kind: 'apiKey' },
    { id: 'openrouterOAuth', displayName: 'Sign in with OpenRouter', kind: 'oauth' }
  ]
}

describe('OpenRouter provider draft', () => {
  it('offers API key and browser login without changing plugin credential flows', () => {
    const options = providerConnectionOptions([backend], (key, vars) => translate('en', key, vars))
    const apiKey = options.find((item) => item.backendId === 'openrouter' && item.authMethod === 'apiKey')!
    const oauth = options.find((item) => item.backendId === 'openrouter' && item.authMethod === 'openrouterOAuth')!
    const draft = applyProviderConnection(createProviderDraft(), apiKey)
    expect(draft.protocol).toBe('auto')
    expect(providerConnectionMode(draft, [backend])).toBe('apiKey')
    expect(providerConnectionMode(applyProviderConnection(draft, oauth), [backend])).toBe('signIn')
    const plugin = { ...backend, id: 'plugin' }
    expect(providerConnectionOptions([plugin], (key, vars) => translate('en', key, vars))
      .find((item) => item.backendId === 'plugin' && item.authMethod === 'apiKey')?.mode).toBe('signIn')
  })

})

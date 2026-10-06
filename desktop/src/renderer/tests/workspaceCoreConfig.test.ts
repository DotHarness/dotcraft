import { describe, expect, it } from 'vitest'
import {
  resolveWorkspaceModelFromConfig,
  resolveWorkspaceProviderFromConfig
} from '../utils/workspaceCoreConfig'

describe('workspace core model resolution', () => {
  const preference = (model: string, speed: 'standard' | 'fast' = 'standard') => ({
    model,
    reasoning: { enabled: false, effort: 'medium' as const, output: 'full' as const },
    speed,
  })

  it('prefers an explicit thread model over a provider-specific workspace model', () => {
    const config = {
      ProviderId: 'provider-a',
      ProviderPreferences: { 'provider-a': preference('remembered-model') }
    }

    expect(resolveWorkspaceModelFromConfig(config, 'provider-a', 'thread-model')).toBe('thread-model')
  })

  it('uses the effective provider model', () => {
    const config = {
      providerid: 'PROVIDER-A',
      providerpreferences: { 'provider-a': preference('remembered-model') }
    }

    const providerId = resolveWorkspaceProviderFromConfig(config)
    expect(providerId).toBe('PROVIDER-A')
    expect(resolveWorkspaceModelFromConfig(config, providerId)).toBe('remembered-model')
  })

  it('falls back to Default when the provider has no preference', () => {
    expect(resolveWorkspaceModelFromConfig({}, 'provider-b')).toBe('Default')
  })
})

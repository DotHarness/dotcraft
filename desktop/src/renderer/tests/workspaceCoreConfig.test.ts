import { describe, expect, it } from 'vitest'
import {
  configObjectFromWorkspaceCore,
  resolveWorkspaceModelFromConfig,
  resolveWorkspaceProviderFromConfig
} from '../utils/workspaceCoreConfig'

describe('workspace core model resolution', () => {
  const preference = (model: string, speed: 'standard' | 'fast' = 'standard') => ({
    model,
    reasoning: { enabled: false, effort: 'medium' as const, output: 'full' as const },
    speed,
    contextWindow: { mode: 'default' as const }
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

  it('ignores the obsolete top-level model and falls back to Default', () => {
    expect(resolveWorkspaceModelFromConfig({ Model: 'legacy-model' }, 'provider-b')).toBe('Default')
    expect(resolveWorkspaceModelFromConfig({ ProviderModels: { 'provider-b': 'legacy' } }, 'provider-b'))
      .toBe('Default')
    expect(resolveWorkspaceModelFromConfig({}, 'provider-b')).toBe('Default')
  })

  it('merges remote provider defaults with case-insensitive workspace overrides', () => {
    const config = configObjectFromWorkspaceCore({
      userDefaults: {
        providerId: 'provider-a',
        providerPreferences: {
          'provider-a': preference('user-model-a', 'fast'),
          'provider-b': preference('user-model-b'),
          'provider-c': preference('user-model-c')
        }
      },
      workspace: {
        providerId: ' Provider-B ',
        providerPreferences: {
          'PROVIDER-A': preference('workspace-model-a'),
          'provider-b': preference('workspace-model-b')
        }
      }
    })

    expect(config).toMatchObject({
      ProviderId: 'Provider-B',
      ProviderPreferences: {
        'PROVIDER-A': preference('workspace-model-a'),
        'provider-b': preference('workspace-model-b')
      }
    })
    expect(config.ProviderPreferences).toHaveProperty('provider-c')
    expect(config.ProviderPreferences).not.toHaveProperty('PROVIDER-C')
    expect(resolveWorkspaceModelFromConfig(config, 'provider-a')).toBe('workspace-model-a')
    expect(resolveWorkspaceModelFromConfig(config, 'PROVIDER-B')).toBe('workspace-model-b')
    expect(resolveWorkspaceModelFromConfig(config, 'provider-c')).toBe('user-model-c')
  })
})

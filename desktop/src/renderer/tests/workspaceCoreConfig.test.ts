import { describe, expect, it } from 'vitest'
import {
  configObjectFromWorkspaceCore,
  resolveWorkspaceModelFromConfig,
  resolveWorkspaceProviderFromConfig
} from '../utils/workspaceCoreConfig'

describe('workspace core model resolution', () => {
  it('prefers an explicit thread model over a provider-specific workspace model', () => {
    const config = {
      ProviderId: 'provider-a',
      Model: 'legacy-model',
      ProviderModels: { 'provider-a': 'remembered-model' }
    }

    expect(resolveWorkspaceModelFromConfig(config, 'provider-a', 'thread-model')).toBe('thread-model')
  })

  it('uses the effective provider model before the legacy top-level model', () => {
    const config = {
      providerid: 'PROVIDER-A',
      model: 'legacy-model',
      providermodels: { 'provider-a': 'remembered-model' }
    }

    const providerId = resolveWorkspaceProviderFromConfig(config)
    expect(providerId).toBe('PROVIDER-A')
    expect(resolveWorkspaceModelFromConfig(config, providerId)).toBe('remembered-model')
  })

  it('falls back to the top-level model and then Default', () => {
    expect(resolveWorkspaceModelFromConfig({ Model: 'legacy-model' }, 'provider-b')).toBe('legacy-model')
    expect(resolveWorkspaceModelFromConfig({ ProviderModels: { 'provider-b': 'default' } }, 'provider-b'))
      .toBe('Default')
    expect(resolveWorkspaceModelFromConfig({}, 'provider-b')).toBe('Default')
  })

  it('merges remote provider defaults with case-insensitive workspace overrides', () => {
    const config = configObjectFromWorkspaceCore({
      userDefaults: {
        providerId: 'provider-a',
        model: 'legacy-model',
        providerModels: {
          'provider-a': 'user-model-a',
          'provider-b': 'user-model-b',
          'provider-c': 'user-model-c',
          ignored: 'Default'
        }
      },
      workspace: {
        providerId: ' Provider-B ',
        model: null,
        providerModels: {
          'PROVIDER-A': 'workspace-model-a',
          'provider-b': ' workspace-model-b ',
          'PROVIDER-C': 'Default'
        }
      }
    })

    expect(config).toMatchObject({
      ProviderId: 'Provider-B',
      Model: 'legacy-model',
      ProviderModels: {
        'PROVIDER-A': 'workspace-model-a',
        'provider-b': 'workspace-model-b'
      }
    })
    expect(config.ProviderModels).not.toHaveProperty('provider-c')
    expect(config.ProviderModels).not.toHaveProperty('PROVIDER-C')
    expect(resolveWorkspaceModelFromConfig(config, 'provider-a')).toBe('workspace-model-a')
    expect(resolveWorkspaceModelFromConfig(config, 'PROVIDER-B')).toBe('workspace-model-b')
    expect(resolveWorkspaceModelFromConfig(config, 'provider-c')).toBe('legacy-model')
  })
})

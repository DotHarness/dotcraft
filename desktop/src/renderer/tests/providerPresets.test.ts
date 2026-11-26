import { describe, expect, it } from 'vitest'
import type { ProviderBackendInfo } from '@dotcraft/sdk/contracts'
import { translate } from '../../shared/locales'
import {
  applyProviderAuthMethod,
  applyProviderPreset,
  createProviderDraft,
  providerConnectionMode,
  providerDraftFromInfo,
  providerFieldsFromDraft,
  providerProblem,
  providerUsable
} from '../components/settings/providers/providerDraft'
import {
  BUILT_IN_BACKENDS,
  findProviderPreset,
  providerPresetIdFor,
  providerPresetMark,
  providerPresets,
  type ProviderPresetId
} from '../components/settings/providers/providerPresets'
import { providerCreateValue } from '../components/settings/providerConfigWrites'
import type { ProviderInfoWire } from '../components/settings/providerInfo'

const t = (key: Parameters<typeof translate>[1], vars?: Record<string, string | number>): string => translate('en', key, vars)
const plugin: ProviderBackendInfo = {
  id: 'plugin', displayName: 'Plugin', defaultProtocol: 'plugin-protocol', protocols: ['plugin-protocol'],
  authenticationMethods: [{ id: 'apiKey', displayName: 'API key', kind: 'apiKey' }]
}
const presets = providerPresets([...BUILT_IN_BACKENDS, plugin], t)
const preset = (id: ProviderPresetId) => findProviderPreset(presets, id)!

function saved(overrides: Partial<ProviderInfoWire>): ProviderInfoWire {
  return { id: 'p', displayName: 'P', protocol: 'openai-responses', hasApiKey: true, endPoint: '', ...overrides }
}

describe('provider availability', () => {
  it('lets users select a service-managed native provider until authentication fails', () => {
    const provider = saved({ managedBy: 'modelService', hasApiKey: false })
    expect(providerUsable(provider)).toBe(true)
    expect(providerProblem(provider, null, t)).toBeNull()

    const signedOut = { ...provider, isAuthenticated: false }
    expect(providerUsable(signedOut)).toBe(false)
    expect(providerProblem(signedOut, null, t)).not.toBeNull()
  })
})

describe('provider presets', () => {
  it('maps saved providers to the preset that describes them', () => {
    expect(providerPresetIdFor(saved({ endPoint: 'https://api.openai.com/v1/' }))).toBe('openai')
    expect(providerPresetIdFor(saved({ protocol: 'anthropic', endPoint: '' }))).toBe('anthropic')
    expect(providerPresetIdFor(saved({ authMethod: 'chatgptOAuth' }))).toBe('chatgpt')
    expect(providerPresetIdFor(saved({ backendId: 'openrouter', protocol: 'auto' }))).toBe('backend:openrouter')
    expect(providerPresetIdFor(saved({ protocol: 'openai-chat-completions', endPoint: 'https://api.openai.com/v1' }))).toBe('custom')
    expect(providerPresetIdFor(saved({ endPoint: 'https://api.deepseek.com' }))).toBe('custom')
    expect(providerPresetIdFor(saved({ protocol: 'anthropic', endPoint: 'https://api.deepseek.com/anthropic/' }))).toBe('deepseek')
    expect(providerDraftFromInfo(saved({ endPoint: 'http://localhost:11434/v1' })).preset).toBe('custom')
  })

  it('marks providers by their preset, then by the API format of a custom endpoint', () => {
    expect(providerPresetMark(saved({ authMethod: 'chatgptOAuth' }))).toBe('openai')
    expect(providerPresetMark(saved({ backendId: 'openrouter', protocol: 'anthropic' }))).toBe('openrouter')
    expect(providerPresetMark(saved({ protocol: 'anthropic', endPoint: 'https://api.deepseek.com/anthropic' }))).toBe('deepseek')
    expect(providerPresetMark(saved({ protocol: 'anthropic', endPoint: 'https://proxy.example/anthropic' }))).toBe('anthropic')
    expect(providerPresetMark(saved({ protocol: 'openai-chat-completions', endPoint: 'http://localhost:11434/v1' }))).toBe('openai')
    expect(providerPresetMark(saved({ backendId: 'review.local', protocol: 'review-local' }))).toBeNull()
  })

  it('writes the configuration of the chosen preset and method', () => {
    const providers = [{ id: 'openrouter' }]
    const draft = applyProviderPreset(createProviderDraft(providers), preset('backend:openrouter'), {
      previous: preset('openai'), providers
    })
    expect(draft).toMatchObject({ id: 'openrouter-2', displayName: 'OpenRouter', backendId: 'openrouter', protocol: 'auto', authMethod: 'apiKey' })
    expect(providerConnectionMode(draft, BUILT_IN_BACKENDS)).toBe('apiKey')

    const signIn = applyProviderAuthMethod({ ...draft, apiKey: 'sk-or' }, preset('backend:openrouter'), 'openrouterOAuth')
    expect(providerConnectionMode(signIn, BUILT_IN_BACKENDS)).toBe('signIn')
    expect(providerCreateValue(providerFieldsFromDraft(signIn, null))).toEqual({
      Protocol: 'auto', DisplayName: 'OpenRouter', BackendId: 'openrouter', BackendSettings: {},
      EndPoint: 'https://openrouter.ai/api/v1', AuthMethod: 'openrouterOAuth'
    })
  })

  it('keeps names the user typed and drops official endpoints for custom services', () => {
    const named = { ...createProviderDraft(), id: 'work', displayName: 'Work', endPoint: 'https://api.openai.com/v1', apiKey: 'sk' }
    const custom = applyProviderPreset(named, preset('custom'), { previous: preset('openai'), providers: [] })
    expect(custom).toMatchObject({ id: 'work', displayName: 'Work', backendId: null, protocol: 'openai-responses', endPoint: '', apiKey: 'sk' })
    const anthropic = applyProviderPreset(createProviderDraft(), preset('anthropic'), { previous: preset('openai'), providers: [] })
    expect(anthropic).toMatchObject({ id: 'anthropic', displayName: 'Anthropic', endPoint: 'https://api.anthropic.com' })
    const deepseek = applyProviderPreset(createProviderDraft(), preset('deepseek'), { previous: preset('openai'), providers: [] })
    expect(deepseek).toMatchObject({ id: 'deepseek', protocol: 'anthropic', endPoint: 'https://api.deepseek.com/anthropic' })
  })

  it('signs plugin backends in even when their method collects a key', () => {
    const draft = applyProviderPreset(createProviderDraft(), preset('backend:plugin'), null)
    expect(providerConnectionMode(draft, [plugin])).toBe('signIn')
  })

  it('resolves plugin backend icons and subtitles from backend descriptors and installed plugins', () => {
    const backends: ProviderBackendInfo[] = [
      ...BUILT_IN_BACKENDS,
      {
        id: 'acme-models',
        displayName: 'Acme Models',
        pluginId: 'acme-models',
        description: 'Custom backend subtitle',
        defaultProtocol: 'acme-cli',
        protocols: ['acme-cli'],
        authenticationMethods: [{ id: 'cli', displayName: 'CLI', kind: 'cli' }]
      },
      {
        id: 'short-desc',
        displayName: 'ShortDesc',
        pluginId: 'short-desc-plugin',
        defaultProtocol: 'proto',
        protocols: ['proto'],
        authenticationMethods: []
      },
      {
        id: 'plain-desc',
        displayName: 'PlainDesc',
        pluginId: 'plain-desc-plugin',
        defaultProtocol: 'proto',
        protocols: ['proto'],
        authenticationMethods: []
      },
      {
        id: 'fallback-only',
        displayName: 'Fallback',
        pluginId: 'missing-plugin',
        defaultProtocol: 'proto',
        protocols: ['proto'],
        authenticationMethods: []
      }
    ]
    const plugins = [
      {
        id: 'acme-models',
        installed: false,
        interface: { composerIconDataUrl: 'data:image/svg+xml,catalog', shortDescription: 'Catalog copy' }
      },
      {
        id: 'acme-models',
        installed: true,
        interface: {
          composerIconDataUrl: 'data:image/svg+xml,installed',
          logoDataUrl: 'data:image/svg+xml,logo',
          shortDescription: 'Plugin short description'
        }
      },
      {
        id: 'short-desc-plugin',
        installed: true,
        description: 'Longer plugin description',
        interface: {
          logoDataUrl: 'data:image/svg+xml,logo-only',
          shortDescription: 'Use your ShortDesc account'
        }
      },
      {
        id: 'plain-desc-plugin',
        installed: true,
        description: 'Plain plugin description'
      }
    ]
    const resolved = providerPresets(backends, t, plugins)
    expect(findProviderPreset(resolved, 'backend:acme-models')).toMatchObject({
      mark: null,
      iconUrl: 'data:image/svg+xml,installed',
      hint: 'Custom backend subtitle'
    })
    expect(findProviderPreset(resolved, 'backend:short-desc')).toMatchObject({
      mark: null,
      iconUrl: 'data:image/svg+xml,logo-only',
      hint: 'Use your ShortDesc account'
    })
    expect(findProviderPreset(resolved, 'backend:plain-desc')).toMatchObject({
      mark: null,
      iconUrl: null,
      hint: 'Plain plugin description'
    })
    expect(findProviderPreset(resolved, 'backend:fallback-only')).toMatchObject({
      mark: null,
      iconUrl: null,
      hint: t('providerPreset.backendHint')
    })
    expect(findProviderPreset(resolved, 'backend:openrouter')).toMatchObject({
      mark: 'openrouter',
      iconUrl: null,
      hint: t('providerPreset.openrouterHint')
    })
  })
})

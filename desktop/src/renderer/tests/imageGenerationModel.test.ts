import { describe, expect, it } from 'vitest'

import {
  canProviderCreateImages,
  resolveImageGenerationTrouble
} from '../components/settings/panels/imageGeneration/imageGenerationModel'
import type { ProviderInfoWire } from '../components/settings/providerInfo'

function provider(overrides: Partial<ProviderInfoWire>): ProviderInfoWire {
  return {
    id: 'p',
    displayName: 'P',
    protocol: 'openai-responses',
    hasApiKey: true,
    endPoint: '',
    supportsImageGeneration: true,
    authMethod: 'apiKey',
    ...overrides
  }
}

describe('canProviderCreateImages', () => {
  it('needs an OpenAI-compatible protocol with image support turned on', () => {
    expect(canProviderCreateImages(provider({}))).toBe(true)
    expect(canProviderCreateImages(provider({ protocol: 'openai-chat-completions' }))).toBe(true)
    expect(canProviderCreateImages(provider({ protocol: 'anthropic' }))).toBe(false)
    expect(canProviderCreateImages(provider({ supportsImageGeneration: false }))).toBe(false)
  })

  it('needs a usable credential', () => {
    expect(canProviderCreateImages(provider({ hasApiKey: false }))).toBe(false)
    expect(canProviderCreateImages(provider({ hasApiKey: false, authMethod: 'chatgptOAuth' }))).toBe(true)
    expect(canProviderCreateImages(provider({ hasApiKey: false, managedBy: 'modelService' }))).toBe(true)
  })
})

describe('resolveImageGenerationTrouble', () => {
  const able = provider({ id: 'able', displayName: 'Able' })
  const unable = provider({ id: 'unable', displayName: 'Unable', protocol: 'anthropic' })

  it('reports nothing while off or before providers load', () => {
    expect(resolveImageGenerationTrouble({ enabled: false, providerId: 'unable' }, [able, unable], 'unable')).toBeNull()
    expect(resolveImageGenerationTrouble({ enabled: true, providerId: '' }, [], 'unable')).toBeNull()
  })

  it('evaluates the workspace provider when following chat', () => {
    expect(resolveImageGenerationTrouble({ enabled: true, providerId: '' }, [able, unable], 'able')).toBeNull()
    expect(resolveImageGenerationTrouble({ enabled: true, providerId: '' }, [able, unable], 'unable'))
      .toEqual({ kind: 'provider', providerId: 'unable', provider: unable })
  })

  it('evaluates the chosen provider, including one that no longer exists', () => {
    expect(resolveImageGenerationTrouble({ enabled: true, providerId: 'able' }, [able, unable], 'unable')).toBeNull()
    expect(resolveImageGenerationTrouble({ enabled: true, providerId: 'gone' }, [able, unable], 'able'))
      .toEqual({ kind: 'provider', providerId: 'gone', provider: null })
  })

  it('reports when no provider can create images', () => {
    expect(resolveImageGenerationTrouble({ enabled: true, providerId: '' }, [unable], 'unable')).toEqual({ kind: 'none' })
  })
})

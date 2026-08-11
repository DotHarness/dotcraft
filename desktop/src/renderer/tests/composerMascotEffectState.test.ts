import { describe, expect, it } from 'vitest'
import { resolveComposerMascotEffectState } from '../components/conversation/composerMascotEffectState'
import type { ModelCatalogItem } from '../stores/modelCatalogStore'

const MODEL: ModelCatalogItem = {
  id: 'gpt-test',
  reasoning: {
    supportsDisable: true,
    supportedEfforts: [
      { effort: 'medium', label: 'Medium', description: '' },
      { effort: 'high', label: 'High', description: '' }
    ],
    defaultEffort: 'medium',
    supportedOutputs: ['full'],
    defaultOutput: 'full'
  },
  speed: {
    supportedModes: ['standard', 'fast'],
    defaultMode: 'standard'
  }
}

describe('resolveComposerMascotEffectState', () => {
  it('resolves model defaults and supported Fast mode', () => {
    expect(resolveComposerMascotEffectState({
      modelName: MODEL.id,
      modelCatalog: [MODEL],
      reasoningValue: 'default',
      speedValue: 'fast',
      contextMode: 'default'
    })).toEqual({
      reasoningEffort: 'medium',
      speed: 'fast',
      contextMax: false
    })
  })

  it('degrades unsupported Fast and keeps degraded MAX visible', () => {
    expect(resolveComposerMascotEffectState({
      modelName: 'unknown-model',
      modelCatalog: [MODEL],
      reasoningValue: 'extraHigh',
      speedValue: 'fast',
      contextMode: 'max',
      contextDegraded: true
    })).toEqual({
      reasoningEffort: 'extraHigh',
      speed: 'standard',
      contextMax: true
    })
  })

  it('maps Ultra to the exact Extra High mascot effect state', () => {
    expect(resolveComposerMascotEffectState({
      modelName: MODEL.id,
      modelCatalog: [MODEL],
      reasoningValue: 'ultra',
      speedValue: 'fast',
      contextMode: 'max'
    })).toEqual({
      reasoningEffort: 'extraHigh',
      speed: 'fast',
      contextMax: true
    })
  })
})

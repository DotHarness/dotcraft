import { describe, expect, it } from 'vitest'
import { readModelPreference, toContractProviderPreferences } from '../../shared/modelPreference'

describe('modelPreference', () => {
  it('round-trips Ultra through the existing provider preference shape', () => {
    const preference = readModelPreference({
      model: 'gpt-5.5',
      reasoning: { enabled: true, effort: 'ULTRA', output: 'full' },
      speed: 'fast',
      contextWindow: { mode: 'max' }
    })

    expect(preference?.reasoning.effort).toBe('ultra')
    expect(toContractProviderPreferences({ openai: preference! })).toEqual({
      openai: expect.objectContaining({
        reasoning: expect.objectContaining({ effort: 'ultra' })
      })
    })
  })
})

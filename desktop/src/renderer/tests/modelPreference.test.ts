import { describe, expect, it } from 'vitest'
import { readModelPreference, toContractProviderPreferences } from '../../shared/modelPreference'

describe('modelPreference', () => {
  it.each(['max', 'ultra'] as const)('round-trips %s through provider preferences', (effort) => {
    const preference = readModelPreference({
      model: 'gpt-5.5',
      reasoning: { enabled: true, effort: effort.toUpperCase(), output: 'full' },
      speed: 'fast'
    })

    expect(preference?.reasoning.effort).toBe(effort)
    expect(toContractProviderPreferences({ openai: preference! })).toEqual({
      openai: expect.objectContaining({
        reasoning: expect.objectContaining({ effort })
      })
    })
  })
})

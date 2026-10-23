import { describe, expect, it } from 'vitest'
import { readModelPreference, toContractProviderPreferences } from '../../shared/modelPreference'

describe('modelPreference', () => {
  it('round-trips max and ultra efforts through provider preferences', () => {
    for (const effort of ['max', 'ultra'] as const) {
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
    }
  })
})

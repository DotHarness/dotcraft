import { describe, expect, it } from 'vitest'
import { readModelPreference } from '../../shared/modelPreference'

describe('modelPreference', () => {
  it('reads max and ultra efforts case-insensitively', () => {
    for (const effort of ['max', 'ultra'] as const) {
      const preference = readModelPreference({
        model: 'gpt-5.5',
        reasoning: { enabled: true, effort: effort.toUpperCase(), output: 'full' },
        speed: 'fast'
      })

      expect(preference?.reasoning.effort).toBe(effort)
    }
  })
})

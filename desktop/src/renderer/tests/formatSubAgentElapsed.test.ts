import { describe, expect, it } from 'vitest'
import { formatSubAgentElapsed } from '../utils/formatSubAgentElapsed'

describe('formatSubAgentElapsed', () => {
  it('formats elapsed milliseconds by unit and clamps negative and non-finite values', () => {
    const cases: Array<[number, string]> = [
      [0, '0s'],
      [59_999, '59s'],
      [65_000, '1m 5s'],
      [3_723_000, '1h 2m 3s'],
      [90_123_000, '1d 1h 2m 3s'],
      [-1_000, '0s'],
      [Number.NaN, '0s']
    ]
    for (const [totalMs, expected] of cases) {
      expect(formatSubAgentElapsed(totalMs)).toBe(expected)
    }
  })
})

import { describe, expect, it } from 'vitest'
import { formatCompactCount } from './formatCompactCount'

describe('formatCompactCount', () => {
  it('formats counts with one-decimal k/M/B suffixes, rolling over at the rounding boundary', () => {
    for (const [value, expected] of [
      [999, '999'],
      [1_000, '1k'],
      [1_250, '1.3k'],
      [999_949, '999.9k'],
      [999_950, '1M'],
      [12_366_500, '12.4M'],
      [1_000_000_000, '1B']
    ] as const) {
      expect(formatCompactCount(value)).toBe(expected)
    }
  })
})

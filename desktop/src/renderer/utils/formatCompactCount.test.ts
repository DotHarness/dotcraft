import { describe, expect, it } from 'vitest'
import { formatCompactCount } from './formatCompactCount'

describe('formatCompactCount', () => {
  it.each([
    [0, '0'],
    [999, '999'],
    [1_000, '1k'],
    [1_250, '1.3k'],
    [999_949, '999.9k'],
    [999_950, '1M'],
    [1_000_000, '1M'],
    [12_366_500, '12.4M'],
    [1_000_000_000, '1B']
  ])('formats %s as %s', (value, expected) => {
    expect(formatCompactCount(value)).toBe(expected)
  })

  it.each([-1, Number.NaN, Number.POSITIVE_INFINITY, Number.NEGATIVE_INFINITY])(
    'treats invalid count %s as zero',
    (value) => {
      expect(formatCompactCount(value)).toBe('0')
    }
  )
})

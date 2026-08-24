import { describe, expect, it } from 'vitest'
import { formatSubAgentElapsed } from '../utils/formatSubAgentElapsed'

describe('formatSubAgentElapsed', () => {
  it.each([
    [0, '0s'],
    [59_999, '59s'],
    [65_000, '1m 5s'],
    [3_723_000, '1h 2m 3s'],
    [90_123_000, '1d 1h 2m 3s']
  ])('formats %i milliseconds as %s', (totalMs, expected) => {
    expect(formatSubAgentElapsed(totalMs)).toBe(expected)
  })

  it('clamps negative and non-finite values', () => {
    expect(formatSubAgentElapsed(-1_000)).toBe('0s')
    expect(formatSubAgentElapsed(Number.NaN)).toBe('0s')
  })
})

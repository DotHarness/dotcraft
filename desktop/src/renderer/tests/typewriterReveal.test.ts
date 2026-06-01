import { describe, it, expect } from 'vitest'
import {
  DEFAULT_REVEAL_PARAMS,
  advanceReveal,
  codePointLength,
  effectiveCps,
  sliceByCodePoints
} from '../utils/typewriterReveal'

describe('effectiveCps', () => {
  it('uses the steady base rate within the catch-up threshold', () => {
    expect(effectiveCps(0)).toBe(DEFAULT_REVEAL_PARAMS.baseCps)
    expect(effectiveCps(DEFAULT_REVEAL_PARAMS.catchupThreshold)).toBe(DEFAULT_REVEAL_PARAMS.baseCps)
  })

  it('accelerates once the backlog exceeds the threshold', () => {
    const cps = effectiveCps(DEFAULT_REVEAL_PARAMS.catchupThreshold + DEFAULT_REVEAL_PARAMS.catchupDivisor)
    // backlog one full divisor past the threshold => base * (1 + 1)
    expect(cps).toBeCloseTo(DEFAULT_REVEAL_PARAMS.baseCps * 2)
  })

  it('never exceeds the catch-up multiplier cap', () => {
    expect(effectiveCps(100_000)).toBe(
      DEFAULT_REVEAL_PARAMS.baseCps * DEFAULT_REVEAL_PARAMS.maxCatchupMultiplier
    )
  })
})

describe('advanceReveal', () => {
  it('moves forward at the base rate when within the catch-up threshold', () => {
    // backlog 50 (<= threshold): steady base rate over 0.1s
    expect(advanceReveal(0, 50, 0.1)).toBeCloseTo(DEFAULT_REVEAL_PARAMS.baseCps * 0.1)
  })

  it('clamps to total and never overshoots', () => {
    expect(advanceReveal(95, 100, 10)).toBe(100)
    expect(advanceReveal(100, 100, 1)).toBe(100)
    expect(advanceReveal(120, 100, 1)).toBe(100)
  })

  it('treats negative dt as no movement', () => {
    expect(advanceReveal(10, 100, -5)).toBe(10)
  })

  it('reveals a large backlog faster than the base rate', () => {
    const steady = advanceReveal(0, 90, 0.1)
    const burst = advanceReveal(0, 5_000, 0.1)
    expect(burst).toBeGreaterThan(steady)
  })
})

describe('codePointLength', () => {
  it('counts astral characters as one code point', () => {
    expect(codePointLength('ab')).toBe(2)
    expect(codePointLength('😀😀')).toBe(2)
    expect(codePointLength('你好')).toBe(2)
  })
})

describe('sliceByCodePoints', () => {
  it('slices without splitting surrogate pairs', () => {
    expect(sliceByCodePoints('😀😀😀', 1)).toBe('😀')
    expect(sliceByCodePoints('😀😀😀', 2)).toBe('😀😀')
  })

  it('clamps out-of-range counts', () => {
    expect(sliceByCodePoints('hello', 0)).toBe('')
    expect(sliceByCodePoints('hello', -3)).toBe('')
    expect(sliceByCodePoints('hello', 99)).toBe('hello')
  })

  it('preserves newlines and whitespace in the prefix', () => {
    expect(sliceByCodePoints('a\nb c', 3)).toBe('a\nb')
  })
})

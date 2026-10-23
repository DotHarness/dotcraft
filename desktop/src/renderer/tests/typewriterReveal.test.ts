import { describe, it, expect } from 'vitest'
import {
  DEFAULT_REVEAL_PARAMS,
  advanceReveal,
  codePointLength,
  effectiveCps,
  sliceByCodePoints
} from '../utils/typewriterReveal'

describe('effectiveCps', () => {
  it('uses the base rate within the threshold, accelerates past it, and caps the multiplier', () => {
    const { baseCps, catchupThreshold, catchupDivisor, maxCatchupMultiplier } = DEFAULT_REVEAL_PARAMS
    expect(effectiveCps(0)).toBe(baseCps)
    expect(effectiveCps(catchupThreshold)).toBe(baseCps)
    expect(effectiveCps(catchupThreshold + catchupDivisor)).toBeCloseTo(baseCps * 2)
    expect(effectiveCps(100_000)).toBe(baseCps * maxCatchupMultiplier)
  })
})

describe('advanceReveal', () => {
  it('advances at the effective rate, clamps to total, and ignores negative dt', () => {
    expect(advanceReveal(0, 50, 0.1)).toBeCloseTo(DEFAULT_REVEAL_PARAMS.baseCps * 0.1)
    expect(advanceReveal(0, 5_000, 0.1)).toBeGreaterThan(advanceReveal(0, 90, 0.1))
    expect(advanceReveal(95, 100, 10)).toBe(100)
    expect(advanceReveal(120, 100, 1)).toBe(100)
    expect(advanceReveal(10, 100, -5)).toBe(10)
  })
})

describe('code point helpers', () => {
  it('counts and slices by code point without splitting surrogate pairs', () => {
    expect(codePointLength('😀😀')).toBe(2)
    expect(codePointLength('你好')).toBe(2)
    expect(sliceByCodePoints('😀😀😀', 2)).toBe('😀😀')
    expect(sliceByCodePoints('a\nb c', 3)).toBe('a\nb')
    expect(sliceByCodePoints('hello', -3)).toBe('')
    expect(sliceByCodePoints('hello', 99)).toBe('hello')
  })
})

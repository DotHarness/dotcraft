import { renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useSpeedTrend } from './speedTrend'

describe('speed trend', () => {
  it('compares each request with the previous one until the value clears', () => {
    const { result, rerender } = renderHook(({ value }) => useSpeedTrend(value), { initialProps: { value: 100 as number | null } })
    expect(result.current.trend).toBeNull()

    rerender({ value: 80 })
    expect(result.current.trend).toBe('down')

    rerender({ value: 82 })
    expect(result.current.trend).toBeNull()

    rerender({ value: null })
    rerender({ value: 50 })
    expect(result.current.trend).toBeNull()
  })
})

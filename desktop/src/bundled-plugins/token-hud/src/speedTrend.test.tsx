import { renderHook } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useCountedValue, useSpeedTrend, type SpeedComparison } from './speedTrend'

describe('speed trend', () => {
  it('shows a cue only for a comparison that crosses the threshold', () => {
    const { result, rerender } = renderHook(({ comparison }) => useSpeedTrend(comparison), {
      initialProps: { comparison: null as SpeedComparison | null }
    })
    expect(result.current.trend).toBeNull()

    rerender({ comparison: { previous: 100, next: 80 } })
    expect(result.current.trend).toBe('down')
    rerender({ comparison: { previous: 80, next: 82 } })
    expect(result.current.trend).toBeNull()
    rerender({ comparison: { previous: 82, next: 96 } })
    expect(result.current.trend).toBe('up')
    rerender({ comparison: null })
    expect(result.current.trend).toBeNull()
  })

  it('restores speed without animating across threads', () => {
    const { result, rerender } = renderHook(({ value, scope }) => useCountedValue(value, scope), {
      initialProps: { value: 30, scope: 'thread-1' }
    })
    rerender({ value: 80, scope: 'thread-2' })
    expect(result.current).toBe(80)
  })
})

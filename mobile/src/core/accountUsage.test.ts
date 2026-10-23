import { describe, expect, it } from 'vitest'
import type { ProviderAuthUsageWindow } from '@dotcraft/sdk/contracts'
import { usageWindows, windowSpan } from './accountUsage'

describe('account usage', () => {
  it('lists each reported window by length with the share left and reset time', () => {
    const windows = usageWindows({
      available: true,
      primary: { usedPercent: 9.4, windowSeconds: 604_800, resetAt: '2026-10-10T00:00:00Z' },
      secondary: { usedPercent: 120, windowSeconds: 18_000, resetAt: '2026-10-04T12:00:00Z' },
    })
    expect(windows).toEqual([
      { seconds: 18_000, percentLeft: 0, resetAt: '2026-10-04T12:00:00Z' },
      { seconds: 604_800, percentLeft: 91, resetAt: '2026-10-10T00:00:00Z' },
    ])
  })

  it('shows nothing for an unavailable snapshot, drops incomplete windows, and names a window by its length', () => {
    expect(usageWindows({ available: false, primary: { usedPercent: 1, windowSeconds: 18_000, resetAt: 'x' } })).toEqual([])
    expect(usageWindows(null)).toEqual([])
    expect(usageWindows({ available: true, primary: { usedPercent: 1, windowSeconds: 18_000 } as ProviderAuthUsageWindow, secondary: null })).toEqual([])
    expect(windowSpan(18_000)).toEqual({ unit: 'hours', count: 5 })
    expect(windowSpan(604_800)).toEqual({ unit: 'days', count: 7 })
    expect(windowSpan(36 * 3600)).toEqual({ unit: 'hours', count: 36 })
  })
})

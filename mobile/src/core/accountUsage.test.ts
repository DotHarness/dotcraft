import { describe, expect, it } from 'vitest'
import { signsInWithAccount, usageWindows, windowSpan } from './accountUsage'

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

  it('shows nothing for an unavailable snapshot and drops incomplete windows', () => {
    expect(usageWindows({ available: false, primary: { usedPercent: 1, windowSeconds: 18_000, resetAt: 'x' } })).toEqual([])
    expect(usageWindows(null)).toEqual([])
    expect(usageWindows({ available: true, primary: { usedPercent: 1, windowSeconds: 18_000 }, secondary: null })).toEqual([])
  })

  it('names a window by its length', () => {
    expect(windowSpan(18_000)).toEqual({ unit: 'hours', count: 5 })
    expect(windowSpan(604_800)).toEqual({ unit: 'days', count: 7 })
    expect(windowSpan(36 * 3600)).toEqual({ unit: 'hours', count: 36 })
  })

  it('treats a provider that signs in with an account as one that reports usage', () => {
    expect(signsInWithAccount('apiKey')).toBe(false)
    expect(signsInWithAccount(undefined)).toBe(false)
    expect(signsInWithAccount('subscriptionOAuth')).toBe(true)
  })
})

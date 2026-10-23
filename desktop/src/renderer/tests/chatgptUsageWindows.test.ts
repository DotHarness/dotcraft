import { describe, expect, it } from 'vitest'
import type { ProviderUsageSnapshot, ProviderUsageWindow } from '../stores/providersStore'
import {
  formatCreditsBalance,
  classifyChatGptUsageWindow,
  shapeChatGptUsageWindows
} from '../utils/chatgptUsageWindows'

function snapshot(
  primary: ProviderUsageWindow | null,
  secondary: ProviderUsageWindow | null
): ProviderUsageSnapshot {
  return {
    available: true,
    planType: 'pro',
    primary,
    secondary,
    credits: null,
    balances: null,
    limitReachedKind: null,
    fetchedAt: '2026-07-13T05:00:00.000Z'
  }
}

function windowWithDuration(windowSeconds: number): ProviderUsageWindow {
  return {
    usedPercent: 2,
    windowSeconds,
    resetAt: '2099-01-01T00:00:00.000Z'
  }
}

describe('ChatGPT usage window shaping', () => {
  it('orders known windows by duration semantics instead of upstream slot', () => {
    const weekly = windowWithDuration(604_800)
    const fiveHour = windowWithDuration(18_000)

    expect(shapeChatGptUsageWindows(snapshot(weekly, fiveHour))).toEqual([
      { kind: 'fiveHour', window: fiveHour },
      { kind: 'weekly', window: weekly }
    ])
    expect(shapeChatGptUsageWindows(snapshot(weekly, null))).toEqual([{ kind: 'weekly', window: weekly }])
    expect(shapeChatGptUsageWindows(snapshot(null, weekly))).toEqual([{ kind: 'weekly', window: weekly }])
  })

  it('classifies durations within five percent and falls back to slot kinds', () => {
    const cases = [[17_100, 'fiveHour'], [18_900, 'fiveHour'], [574_560, 'weekly'], [635_040, 'weekly']] as const
    for (const [windowSeconds, expected] of cases) {
      expect(classifyChatGptUsageWindow(windowWithDuration(windowSeconds), 'primary')).toBe(expected)
    }

    expect(shapeChatGptUsageWindows(snapshot(
      windowWithDuration(30 * 24 * 60 * 60),
      windowWithDuration(60 * 60)
    )).map((entry) => entry.kind)).toEqual(['primary', 'secondary'])
  })
})

describe('Credits balance formatting', () => {
  it('drops trailing zeros, groups digits for the locale and keeps non-numbers as sent', () => {
    expect(formatCreditsBalance('62500.0000000000', 'en')).toBe('62,500')
    expect(formatCreditsBalance('12.3456', 'en')).toBe('12.35')
    expect(formatCreditsBalance('62500.5', 'de')).toBe('62.500,5')
    expect(formatCreditsBalance('n/a', 'en')).toBe('n/a')
    expect(formatCreditsBalance('', 'en')).toBe('')
  })
})

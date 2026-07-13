import { describe, expect, it } from 'vitest'
import type { ChatGptUsageSnapshot, ChatGptUsageWindow } from '../stores/providersStore'
import {
  classifyChatGptUsageWindow,
  shapeChatGptUsageWindows
} from '../utils/chatgptUsageWindows'

describe('ChatGPT usage window shaping', () => {
  it('orders known windows by duration semantics instead of upstream slot', () => {
    const weekly = windowWithDuration(604_800)
    const fiveHour = windowWithDuration(18_000)

    expect(shapeChatGptUsageWindows(snapshot(weekly, fiveHour))).toEqual([
      { kind: 'fiveHour', window: fiveHour },
      { kind: 'weekly', window: weekly }
    ])
  })

  it.each([
    ['primary', snapshot(windowWithDuration(604_800), null)],
    ['secondary', snapshot(null, windowWithDuration(604_800))]
  ])('recognizes a weekly-only %s slot', (_slot, usage) => {
    expect(shapeChatGptUsageWindows(usage)).toEqual([
      { kind: 'weekly', window: usage.primary ?? usage.secondary }
    ])
  })

  it.each([
    [17_100, 'fiveHour'],
    [18_900, 'fiveHour'],
    [574_560, 'weekly'],
    [635_040, 'weekly']
  ] as const)('accepts the five-percent tolerance for %s seconds', (windowSeconds, expected) => {
    expect(classifyChatGptUsageWindow(windowWithDuration(windowSeconds), 'primary')).toBe(expected)
  })

  it('uses generic slot kinds for unknown durations', () => {
    expect(shapeChatGptUsageWindows(snapshot(
      windowWithDuration(30 * 24 * 60 * 60),
      windowWithDuration(60 * 60)
    )).map((entry) => entry.kind)).toEqual(['primary', 'secondary'])
  })
})

function snapshot(
  primary: ChatGptUsageWindow | null,
  secondary: ChatGptUsageWindow | null
): ChatGptUsageSnapshot {
  return {
    available: true,
    planType: 'pro',
    primary,
    secondary,
    credits: null,
    limitReachedKind: null,
    fetchedAt: '2026-07-13T05:00:00.000Z'
  }
}

function windowWithDuration(windowSeconds: number): ChatGptUsageWindow {
  return {
    usedPercent: 2,
    windowSeconds,
    resetAt: '2099-01-01T00:00:00.000Z'
  }
}

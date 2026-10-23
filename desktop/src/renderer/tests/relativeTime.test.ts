import { describe, it, expect } from 'vitest'
import { formatRelativeTime } from '../utils/relativeTime'

const now = new Date('2024-06-15T14:00:00Z')
const MINUTE = 60 * 1000
const HOUR = 60 * MINUTE
const DAY = 24 * HOUR

function ago(ms: number): string {
  return new Date(now.getTime() - ms).toISOString()
}

describe('formatRelativeTime', () => {
  it('picks the unit at each boundary', () => {
    const cases: Array<[number, string]> = [
      [59 * 1000, 'just now'],
      [MINUTE, '1m'],
      [59 * MINUTE, '59m'],
      [HOUR, '1h'],
      [23 * HOUR, '23h'],
      [DAY, '1d'],
      [6 * DAY, '6d'],
      [7 * DAY, '1w'],
      [28 * DAY, '4w'],
      [30 * DAY, '1mo'],
      [360 * DAY, '12mo']
    ]
    for (const [ms, expected] of cases) {
      expect(formatRelativeTime(ago(ms), now)).toBe(expected)
    }
  })

  it('localizes units and the just-now case', () => {
    expect(formatRelativeTime(ago(5 * MINUTE), now, 'zh-Hans')).toBe('5分钟')
    expect(formatRelativeTime(ago(3 * HOUR), now, 'ja')).toBe('3時間')
    expect(formatRelativeTime(ago(2 * DAY), now, 'fr')).toBe('2 j')
    for (const locale of ['zh-Hans', 'ja', 'ko', 'es', 'fr', 'de'] as const) {
      expect(formatRelativeTime(ago(30 * 1000), now, locale)).not.toBe('just now')
    }
  })
})

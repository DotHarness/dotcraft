import { describe, it, expect } from 'vitest'
import { formatRelativeTime } from '../utils/relativeTime'

const now = new Date('2024-06-15T14:00:00Z')

function msAgo(ms: number): string {
  return new Date(now.getTime() - ms).toISOString()
}
function minutesAgo(m: number): string { return msAgo(m * 60 * 1000) }
function hoursAgo(h: number): string { return msAgo(h * 60 * 60 * 1000) }
function daysAgo(d: number): string { return msAgo(d * 24 * 60 * 60 * 1000) }
function weeksAgo(w: number): string { return daysAgo(w * 7) }
function monthsAgo(mo: number): string { return daysAgo(mo * 30) }

describe('formatRelativeTime', () => {
  it('returns "just now" for less than 60 seconds ago', () => {
    expect(formatRelativeTime(msAgo(30 * 1000), now)).toBe('just now')
    expect(formatRelativeTime(msAgo(59 * 1000), now)).toBe('just now')
  })

  it('formats 30 minutes ago as "30m"', () => {
    expect(formatRelativeTime(minutesAgo(30), now)).toBe('30m')
  })

  it('formats 1 minute ago as "1m"', () => {
    expect(formatRelativeTime(minutesAgo(1), now)).toBe('1m')
  })

  it('formats 59 minutes ago as "59m"', () => {
    expect(formatRelativeTime(minutesAgo(59), now)).toBe('59m')
  })

  it('formats 2 hours ago as "2h"', () => {
    expect(formatRelativeTime(hoursAgo(2), now)).toBe('2h')
  })

  it('formats 1 hour ago as "1h"', () => {
    expect(formatRelativeTime(hoursAgo(1), now)).toBe('1h')
  })

  it('formats 23 hours ago as "23h"', () => {
    expect(formatRelativeTime(hoursAgo(23), now)).toBe('23h')
  })

  it('formats 1 day ago as "1d"', () => {
    expect(formatRelativeTime(daysAgo(1), now)).toBe('1d')
  })

  it('formats 3 days ago as "3d"', () => {
    expect(formatRelativeTime(daysAgo(3), now)).toBe('3d')
  })

  it('formats 6 days ago as "6d"', () => {
    expect(formatRelativeTime(daysAgo(6), now)).toBe('6d')
  })

  it('formats 2 weeks ago as "2w"', () => {
    expect(formatRelativeTime(weeksAgo(2), now)).toBe('2w')
  })

  it('formats 1 week ago as "1w"', () => {
    expect(formatRelativeTime(weeksAgo(1), now)).toBe('1w')
  })

  it('formats 4 weeks ago as "4w"', () => {
    expect(formatRelativeTime(weeksAgo(4), now)).toBe('4w')
  })

  it('formats 2 months ago as "2mo"', () => {
    expect(formatRelativeTime(monthsAgo(2), now)).toBe('2mo')
  })

  it('formats 1 month ago as "1mo"', () => {
    expect(formatRelativeTime(monthsAgo(1), now)).toBe('1mo')
  })

  it('formats 12 months ago as "12mo"', () => {
    expect(formatRelativeTime(monthsAgo(12), now)).toBe('12mo')
  })

  it('uses Intl-style output for zh-Hans', () => {
    const s = formatRelativeTime(minutesAgo(5), now, 'zh-Hans')
    expect(s.length).toBeGreaterThan(0)
    expect(s).not.toMatch(/^\d+m$/)
  })
})

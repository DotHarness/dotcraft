import { describe, it, expect } from 'vitest'
import { groupThreads, classifyThread } from '../utils/threadGrouping'
import type { ThreadSummary } from '../types/thread'

function makeThread(id: string, lastActiveAt: string): ThreadSummary {
  return {
    id,
    displayName: `Thread ${id}`,
    status: 'active',
    originChannel: 'test',
    createdAt: lastActiveAt,
    lastActiveAt
  }
}

/** Returns a date shifted by the given number of days from the reference. */
function daysAgo(days: number, ref: Date = new Date()): string {
  const d = new Date(ref)
  d.setDate(d.getDate() - days)
  return d.toISOString()
}

describe('classifyThread', () => {
  const now = new Date('2024-06-15T14:00:00Z')

  it('classifies a thread active today as "Today"', () => {
    // 2 hours ago
    const date = new Date(now.getTime() - 2 * 60 * 60 * 1000).toISOString()
    expect(classifyThread(date, now)).toBe('Today')
  })

  it('classifies a thread at exactly start of today as "Today"', () => {
    const startOfDay = new Date(2024, 5, 15, 0, 0, 0).toISOString()
    expect(classifyThread(startOfDay, now)).toBe('Today')
  })

  it('classifies a thread at 23:59:59 yesterday as "Yesterday"', () => {
    const yesterday2359 = new Date(2024, 5, 14, 23, 59, 59).toISOString()
    expect(classifyThread(yesterday2359, now)).toBe('Yesterday')
  })

  it('classifies a thread from 2 days ago as "Previous 7 Days"', () => {
    const twoDaysAgo = new Date(2024, 5, 13, 12, 0, 0).toISOString()
    expect(classifyThread(twoDaysAgo, now)).toBe('Previous 7 Days')
  })

  it('classifies a thread from 6 days ago as "Previous 7 Days"', () => {
    const sixDaysAgo = new Date(2024, 5, 9, 0, 0, 1).toISOString()
    expect(classifyThread(sixDaysAgo, now)).toBe('Previous 7 Days')
  })

  it('classifies a thread from 8 days ago as "Previous 30 Days"', () => {
    const eightDaysAgo = new Date(2024, 5, 7, 12, 0, 0).toISOString()
    expect(classifyThread(eightDaysAgo, now)).toBe('Previous 30 Days')
  })

  it('classifies a thread from 31 days ago as "Older"', () => {
    const thirtyOneDaysAgo = new Date(2024, 4, 15, 12, 0, 0).toISOString()
    expect(classifyThread(thirtyOneDaysAgo, now)).toBe('Older')
  })
})

describe('groupThreads', () => {
  const now = new Date('2024-06-15T14:00:00Z')

  it('returns a Map with only the groups that have threads', () => {
    const threads = [makeThread('t1', daysAgo(0, now))]
    const groups = groupThreads(threads, now)
    expect(groups.size).toBe(1)
    expect(groups.has('Today')).toBe(true)
  })

  it('excludes empty groups from the result', () => {
    const threads = [makeThread('t1', daysAgo(2, now))]
    const groups = groupThreads(threads, now)
    expect(groups.has('Today')).toBe(false)
    expect(groups.has('Yesterday')).toBe(false)
    expect(groups.has('Previous 7 Days')).toBe(true)
  })

  it('places threads into correct groups', () => {
    const t1 = makeThread('today', new Date(now.getTime() - 1 * 60 * 60 * 1000).toISOString())
    const t2 = makeThread('yesterday', new Date(2024, 5, 14, 10, 0, 0).toISOString())
    const t3 = makeThread('prev7', new Date(2024, 5, 10, 12, 0, 0).toISOString())
    const t4 = makeThread('prev30', new Date(2024, 5, 1, 12, 0, 0).toISOString())
    const t5 = makeThread('older', new Date(2024, 4, 1, 12, 0, 0).toISOString())

    const groups = groupThreads([t1, t2, t3, t4, t5], now)

    expect(groups.get('Today')).toEqual([t1])
    expect(groups.get('Yesterday')).toEqual([t2])
    expect(groups.get('Previous 7 Days')).toEqual([t3])
    expect(groups.get('Previous 30 Days')).toEqual([t4])
    expect(groups.get('Older')).toEqual([t5])
  })

  it('preserves original thread order within a group', () => {
    const threads = [
      makeThread('a', new Date(2024, 5, 15, 13, 0, 0).toISOString()),
      makeThread('b', new Date(2024, 5, 15, 12, 0, 0).toISOString()),
      makeThread('c', new Date(2024, 5, 15, 11, 0, 0).toISOString())
    ]
    const groups = groupThreads(threads, now)
    const today = groups.get('Today')!
    expect(today.map((t) => t.id)).toEqual(['a', 'b', 'c'])
  })

  it('returns groups in canonical order', () => {
    const threads = [
      makeThread('old', new Date(2024, 4, 1).toISOString()),
      makeThread('today', new Date(2024, 5, 15, 12, 0, 0).toISOString())
    ]
    const groups = groupThreads(threads, now)
    const keys = [...groups.keys()]
    expect(keys.indexOf('Today')).toBeLessThan(keys.indexOf('Older'))
  })

  it('returns an empty Map for an empty thread list', () => {
    expect(groupThreads([], now).size).toBe(0)
  })
})

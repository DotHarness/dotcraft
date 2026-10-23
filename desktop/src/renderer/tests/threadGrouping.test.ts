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

const now = new Date('2024-06-15T14:00:00Z')

describe('classifyThread', () => {
  it('classifies by local calendar-day boundaries', () => {
    for (const [date, expected] of [
      [new Date(now.getTime() - 2 * 60 * 60 * 1000), 'Today'],
      [new Date(2024, 5, 15, 0, 0, 0), 'Today'],
      [new Date(2024, 5, 14, 23, 59, 59), 'Yesterday'],
      [new Date(2024, 5, 13, 12, 0, 0), 'Previous 7 Days'],
      [new Date(2024, 5, 9, 0, 0, 1), 'Previous 7 Days'],
      [new Date(2024, 5, 7, 12, 0, 0), 'Previous 30 Days'],
      [new Date(2024, 4, 15, 12, 0, 0), 'Older']
    ] as const) {
      expect(classifyThread(date.toISOString(), now), date.toISOString()).toBe(expected)
    }
  })
})

describe('groupThreads', () => {
  it('places threads into non-empty groups in canonical order, preserving order within a group', () => {
    const t1 = makeThread('today', new Date(now.getTime() - 1 * 60 * 60 * 1000).toISOString())
    const t1b = makeThread('today-b', new Date(2024, 5, 15, 12, 0, 0).toISOString())
    const t4 = makeThread('prev30', new Date(2024, 5, 1, 12, 0, 0).toISOString())
    const t5 = makeThread('older', new Date(2024, 4, 1, 12, 0, 0).toISOString())

    const groups = groupThreads([t5, t1, t4, t1b], now)

    expect([...groups.keys()]).toEqual(['Today', 'Previous 30 Days', 'Older'])
    expect(groups.get('Today')).toEqual([t1, t1b])
    expect(groups.get('Previous 30 Days')).toEqual([t4])
    expect(groups.get('Older')).toEqual([t5])
    expect(groupThreads([], now).size).toBe(0)
  })
})

import { describe, expect, it } from 'vitest'
import { moveThreadId, orderThreadsBySortMode } from '../components/sidebar/threadOrdering'
import type { ThreadSummary } from '../types/thread'

function thread(id: string, createdAt: string, lastActiveAt: string): ThreadSummary {
  return {
    id,
    displayName: id,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt,
    lastActiveAt
  }
}

const threads = [
  thread('old-busy', '2026-09-01T00:00:00.000Z', '2026-09-28T12:00:00.000Z'),
  thread('mid', '2026-09-10T00:00:00.000Z', '2026-09-20T00:00:00.000Z'),
  thread('new-quiet', '2026-09-27T00:00:00.000Z', '2026-09-27T00:00:00.000Z')
]

describe('orderThreadsBySortMode', () => {
  it('orders by latest activity when sorting by last updated', () => {
    expect(orderThreadsBySortMode(threads, 'updated', ['mid']).map((t) => t.id))
      .toEqual(['old-busy', 'new-quiet', 'mid'])
  })

  it('keeps the saved order regardless of activity in manual mode', () => {
    expect(orderThreadsBySortMode(threads, 'manual', ['mid', 'new-quiet', 'old-busy']).map((t) => t.id))
      .toEqual(['mid', 'new-quiet', 'old-busy'])
  })

  it('places threads without a saved position first, newest created first', () => {
    expect(orderThreadsBySortMode(threads, 'manual', ['mid', 'gone']).map((t) => t.id))
      .toEqual(['new-quiet', 'old-busy', 'mid'])
  })
})

describe('moveThreadId', () => {
  it('inserts the moved id before or after the target', () => {
    expect(moveThreadId(['a', 'b', 'c', 'd'], 'd', 'b', 'before')).toEqual(['a', 'd', 'b', 'c'])
    expect(moveThreadId(['a', 'b', 'c', 'd'], 'a', 'c', 'after')).toEqual(['b', 'c', 'a', 'd'])
  })

  it('leaves the order unchanged for a self drop or an unknown target', () => {
    expect(moveThreadId(['a', 'b'], 'a', 'a', 'after')).toEqual(['a', 'b'])
    expect(moveThreadId(['a', 'b'], 'a', 'z', 'before')).toEqual(['a', 'b'])
  })
})

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
  it('orders by activity when updated, by saved order when manual, with unsaved threads first by creation', () => {
    expect(orderThreadsBySortMode(threads, 'updated', ['mid']).map((t) => t.id))
      .toEqual(['old-busy', 'new-quiet', 'mid'])
    expect(orderThreadsBySortMode(threads, 'manual', ['mid', 'new-quiet', 'old-busy']).map((t) => t.id))
      .toEqual(['mid', 'new-quiet', 'old-busy'])
    expect(orderThreadsBySortMode(threads, 'manual', ['mid', 'gone']).map((t) => t.id))
      .toEqual(['new-quiet', 'old-busy', 'mid'])
  })
})

describe('moveThreadId', () => {
  it('inserts the moved id before or after the target, ignoring self drops and unknown targets', () => {
    expect(moveThreadId(['a', 'b', 'c', 'd'], 'd', 'b', 'before')).toEqual(['a', 'd', 'b', 'c'])
    expect(moveThreadId(['a', 'b', 'c', 'd'], 'a', 'c', 'after')).toEqual(['b', 'c', 'a', 'd'])
    expect(moveThreadId(['a', 'b'], 'a', 'a', 'after')).toEqual(['a', 'b'])
    expect(moveThreadId(['a', 'b'], 'a', 'z', 'before')).toEqual(['a', 'b'])
  })
})

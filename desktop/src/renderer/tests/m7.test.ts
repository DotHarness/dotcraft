import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { useToastStore } from '../stores/toastStore'
import { useThreadStore } from '../stores/threadStore'

const ts = () => useToastStore.getState()
const thr = () => useThreadStore.getState()

beforeEach(() => {
  useToastStore.setState({ toasts: [] })
  thr().reset()
  vi.useFakeTimers()
})

afterEach(() => {
  vi.useRealTimers()
})

describe('toastStore', () => {
  it('stacks toasts with unique ids and keeps them until removed', () => {
    ts().addToast('first', 'info', 3000)
    ts().addToast('second', 'error', 5000)

    expect(ts().toasts).toMatchObject([
      { message: 'first', type: 'info', duration: 3000 },
      { message: 'second', type: 'error', duration: 5000 }
    ])
    expect(ts().toasts[0].id).toMatch(/^toast-/)
    expect(new Set(ts().toasts.map((toast) => toast.id)).size).toBe(2)

    vi.advanceTimersByTime(5001)
    expect(ts().toasts).toHaveLength(2)

    ts().removeToast(ts().toasts[0].id)
    expect(ts().toasts.map((toast) => toast.message)).toEqual(['second'])
  })
})

describe('threadStore', () => {
  it('tracks running turns per thread and clears threads and running turns on reset', () => {
    thr().setThreadList([
      { id: 'thread-1', displayName: 'Test', status: 'active', lastActiveAt: new Date().toISOString() }
    ])
    thr().setActiveThreadId('thread-1')
    thr().markTurnStarted('t1')
    thr().markTurnStarted('t2')
    thr().markTurnStarted('t3')
    thr().markTurnEnded('t2')
    thr().markTurnEnded('unknown')

    expect([...thr().runningTurnThreadIds].sort()).toEqual(['t1', 't3'])

    thr().reset()

    expect(thr().threadList).toHaveLength(0)
    expect(thr().activeThreadId).toBeNull()
    expect(thr().activeThread).toBeNull()
    expect(thr().runningTurnThreadIds.size).toBe(0)
  })
})

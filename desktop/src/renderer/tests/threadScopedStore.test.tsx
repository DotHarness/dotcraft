import { act, renderHook } from '@testing-library/react'
import { createStore } from 'zustand'
import { describe, expect, it, vi } from 'vitest'
import { createThreadScopedStores } from '../stores/threadScopedStore'

function counters() {
  return createThreadScopedStores<{ count: number }>(() => createStore(() => ({ count: 0 })))
}

describe('thread-scoped stores', () => {
  it('reads and notifies only through the active store', () => {
    const stores = counters()
    const first = stores.create('first')
    const second = stores.create('second')
    stores.activate('first')
    const listener = vi.fn()
    stores.useActive.subscribe(listener)
    const { result } = renderHook(() => stores.useActive((state) => state.count))

    act(() => { second.setState({ count: 2 }) })
    expect(listener).not.toHaveBeenCalled()
    expect(result.current).toBe(0)

    act(() => { stores.activate('second') })
    expect(result.current).toBe(2)
    expect(listener).toHaveBeenLastCalledWith({ count: 2 }, { count: 0 })

    act(() => { first.setState({ count: 1 }) })
    expect(listener).toHaveBeenCalledTimes(1)

    act(() => { stores.useActive.setState({ count: 3 }) })
    expect(second.getState().count).toBe(3)
    expect(result.current).toBe(3)
  })

  it('starts a released thread from a fresh store', () => {
    const stores = counters()
    stores.create('thread').setState({ count: 5 })
    stores.release('thread')

    expect(stores.get('thread')).toBeUndefined()
    expect(stores.activate('thread').getState().count).toBe(0)
    expect(stores.get('thread')).toBeUndefined()
  })
})

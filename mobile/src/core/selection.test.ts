import { describe, expect, it } from 'vitest'
import { selectStore, shallowEqual } from './selection'
import { createStore } from './store'
import { selectComputerStore } from './computerSelection'
import { initialState, reducer, type ComputerRecord } from './state'

describe('selected subscriptions', () => {
  it('keeps the final selected computer snapshot readable until its view unmounts on removal', () => {
    const computer: ComputerRecord = {
      id: 'pc',
      name: 'Before',
      version: null,
      port: 47610,
      fingerprint: 'f',
      addresses: [],
      relay: null,
      lastAddress: null,
      deviceId: 'device',
      pairedAt: '',
    }
    const store = createStore(reducer, reducer(initialState(), { type: 'paired', computer }))
    const selected = selectComputerStore(store, 'pc', (state) => state.computer.name, Object.is)
    const seen: string[] = []
    const stop = selected.subscribe(() => seen.push(selected.getState()))
    store.dispatch({ type: 'computer', computerId: 'pc', action: { type: 'computerSeen', patch: { name: 'Latest' } } })
    store.dispatch({ type: 'forgotten', computerId: 'pc', revoked: false })
    expect(selected.getState()).toBe('Latest')
    expect(seen).toEqual(['Latest'])
    stop()
  })

  it('ignores unrelated updates and keeps an equal snapshot stable', () => {
    const store = createStore(
      (state: { text: string; usage: number }, patch: Partial<{ text: string; usage: number }>) => ({ ...state, ...patch }),
      { text: '', usage: 0 },
    )
    const messages = selectStore(store, (state) => ({ text: state.text }), shallowEqual)
    const initial = messages.getState()
    const seen: string[] = []
    const stop = messages.subscribe(() => seen.push(messages.getState().text))
    store.dispatch({ usage: 1 })
    expect(messages.getState()).toBe(initial)
    store.dispatch({ text: 'hello' })
    store.dispatch({ usage: 2 })
    expect(seen).toEqual(['hello'])
    stop()
    store.dispatch({ text: 'later' })
    expect(seen).toEqual(['hello'])
    expect(messages.getState().text).toBe('later')
  })

  it('notifies when selected keys change even if their values are undefined', () => {
    const store = createStore((_state: Record<string, undefined>, next: Record<string, undefined>) => next, { a: undefined })
    const selected = selectStore(store, (state) => state, shallowEqual)
    const seen: string[][] = []
    selected.subscribe(() => seen.push(Object.keys(selected.getState())))
    store.dispatch({ b: undefined })
    expect(Object.keys(selected.getState())).toEqual(['b'])
    expect(seen).toEqual([['b']])
  })

  it('notifies each listener even if a snapshot is read before its callback', () => {
    const store = createStore((_state: number, value: number) => value, 0)
    const selected = selectStore(store, (state) => state)
    const values: number[] = []
    store.subscribe(() => selected.getState())
    selected.subscribe(() => values.push(selected.getState()))
    store.dispatch(1)
    expect(values).toEqual([1])
  })
})

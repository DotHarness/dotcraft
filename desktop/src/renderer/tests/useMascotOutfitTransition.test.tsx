import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, renderHook } from '@testing-library/react'
import { MASCOT_OUTFIT_DURATION_MS, MASCOT_OUTFIT_SWAP_MS } from '@dotcraft/avatar/react'
import { useMascotOutfitTransition } from '../components/conversation/useMascotOutfitTransition'

const OUTFIT = { name: 'creator' }

beforeEach(() => {
  vi.useFakeTimers()
  document.documentElement.dataset.reduceMotion = 'off'
})

afterEach(() => {
  vi.useRealTimers()
  delete document.documentElement.dataset.reduceMotion
})

function setup(initial: typeof OUTFIT | undefined) {
  return renderHook(({ outfit, signal }) => useMascotOutfitTransition(outfit, signal), {
    initialProps: { outfit: initial, signal: 0 }
  })
}

describe('mascot outfit transition', () => {
  it('swaps at once when the outfit changes without a toggle, as on load or a thread switch', () => {
    const hook = setup(undefined)
    hook.rerender({ outfit: OUTFIT, signal: 0 })
    expect(hook.result.current).toEqual({ outfit: OUTFIT, phase: null, spin: 0 })
  })

  it('plays the suit-up for a toggle and swaps the outfit partway through', () => {
    const hook = setup(undefined)
    hook.rerender({ outfit: OUTFIT, signal: 1 })
    expect(hook.result.current).toMatchObject({ outfit: undefined, spin: 1 })

    act(() => { vi.advanceTimersByTime(MASCOT_OUTFIT_SWAP_MS) })
    expect(hook.result.current).toMatchObject({ outfit: OUTFIT, phase: 'in' })

    act(() => { vi.advanceTimersByTime(MASCOT_OUTFIT_DURATION_MS) })
    expect(hook.result.current.phase).toBeNull()
  })

  it('keeps the outfit on during the take-off until the swap', () => {
    const hook = setup(OUTFIT)
    hook.rerender({ outfit: undefined, signal: 1 })
    expect(hook.result.current).toMatchObject({ outfit: OUTFIT, phase: 'out' })

    act(() => { vi.advanceTimersByTime(MASCOT_OUTFIT_SWAP_MS) })
    expect(hook.result.current).toMatchObject({ outfit: undefined, phase: null })
  })

  it('swaps at once under reduced motion', () => {
    document.documentElement.dataset.reduceMotion = 'on'
    const hook = setup(undefined)
    hook.rerender({ outfit: OUTFIT, signal: 1 })
    expect(hook.result.current).toEqual({ outfit: OUTFIT, phase: null, spin: 0 })
  })
})

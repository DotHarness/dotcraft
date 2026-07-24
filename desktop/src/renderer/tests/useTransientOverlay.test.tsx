import { act, renderHook } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { LayerContext } from '../contexts/LayerContext'
import { useTransientOverlay } from '../hooks/useTransientOverlay'
import { useTransientOverlayStore } from '../stores/transientOverlayStore'

function wrapperAt(depth: number) {
  return ({ children }: { children: ReactNode }): JSX.Element => (
    <LayerContext.Provider value={depth}>{children}</LayerContext.Provider>
  )
}

afterEach(() => {
  act(() => {
    useTransientOverlayStore.setState({ openDepths: [], topDepth: 0 })
  })
})

describe('useTransientOverlay', () => {
  it('opens through the gated open()', () => {
    const { result } = renderHook(() => useTransientOverlay(), { wrapper: wrapperAt(0) })
    expect(result.current.visible).toBe(false)
    act(() => result.current.open())
    expect(result.current.visible).toBe(true)
  })

  it('closes and blocks opening while a deeper layer is registered, then re-allows after it pops', () => {
    const { result } = renderHook(() => useTransientOverlay(), { wrapper: wrapperAt(0) })
    act(() => result.current.open())
    expect(result.current.visible).toBe(true)

    // A layer opened above (depth 1 > our depth 0) suppresses us — no pointer move needed.
    act(() => useTransientOverlayStore.getState().pushLayer(1))
    expect(result.current.blocked).toBe(true)
    expect(result.current.visible).toBe(false)

    // Opening is a no-op while blocked (no path bypasses the gate).
    act(() => result.current.open())
    expect(result.current.visible).toBe(false)

    act(() => useTransientOverlayStore.getState().popLayer(1))
    expect(result.current.blocked).toBe(false)
    act(() => result.current.open())
    expect(result.current.visible).toBe(true)
  })

  it('is not suppressed by a same-depth layer (a tooltip inside that modal still shows)', () => {
    const { result } = renderHook(() => useTransientOverlay(), { wrapper: wrapperAt(1) })
    act(() => result.current.open())
    act(() => useTransientOverlayStore.getState().pushLayer(1))
    expect(result.current.blocked).toBe(false)
    expect(result.current.visible).toBe(true)
  })

  it('dismisses on scroll, Escape, and outside pointerdown', () => {
    const { result } = renderHook(() => useTransientOverlay(), { wrapper: wrapperAt(0) })

    act(() => result.current.open())
    act(() => document.dispatchEvent(new Event('scroll')))
    expect(result.current.visible).toBe(false)

    act(() => result.current.open())
    act(() => document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape' })))
    expect(result.current.visible).toBe(false)

    act(() => result.current.open())
    act(() => document.dispatchEvent(new Event('pointerdown')))
    expect(result.current.visible).toBe(false)
  })

  it('interactive: cancelClose() keeps it open past the close delay', () => {
    vi.useFakeTimers()
    try {
      const { result } = renderHook(
        () => useTransientOverlay({ interactive: true, closeDelayMs: 120 }),
        { wrapper: wrapperAt(0) }
      )
      act(() => result.current.open())
      act(() => result.current.scheduleClose())
      act(() => result.current.cancelClose())
      act(() => vi.advanceTimersByTime(200))
      expect(result.current.visible).toBe(true)
    } finally {
      vi.useRealTimers()
    }
  })
})

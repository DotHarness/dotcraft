import { act, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useMenuAim, type MenuAimPoint, type MenuAimSide } from '../hooks/useMenuAim'

describe('useMenuAim', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
    vi.restoreAllMocks()
  })

  it.each([
    ['right', { clientX: 100, clientY: 100 }, { clientX: 150, clientY: 125 }, domRect(200, 50, 100, 100)],
    ['left', { clientX: 300, clientY: 100 }, { clientX: 250, clientY: 125 }, domRect(100, 50, 100, 100)]
  ] satisfies Array<[MenuAimSide, MenuAimPoint, MenuAimPoint, DOMRect]>)(
    'delays an action while the pointer travels through a %s-opening cone',
    (side, anchor, point, rect) => {
      const submenu = createSubmenu(rect)
      const action = vi.fn()
      const { result } = renderHook(() => useMenuAim({ submenuRef: { current: submenu }, side }))

      act(() => {
        result.current.track(anchor)
        result.current.guard(point, action)
      })

      expect(action).not.toHaveBeenCalled()
      act(() => vi.advanceTimersByTime(279))
      expect(action).not.toHaveBeenCalled()
      act(() => vi.advanceTimersByTime(1))
      expect(action).toHaveBeenCalledOnce()
    }
  )

  it('runs immediately when the pointer leaves the prediction cone', () => {
    const submenu = createSubmenu(domRect(200, 50, 100, 100))
    const action = vi.fn()
    const { result } = renderHook(() => useMenuAim({ submenuRef: { current: submenu }, side: 'right' }))

    act(() => {
      result.current.track({ clientX: 100, clientY: 100 })
      result.current.guard({ clientX: 150, clientY: 190 }, action)
    })

    expect(action).toHaveBeenCalledOnce()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('keeps the guard active through the four-pixel submenu seam tolerance', () => {
    const submenu = createSubmenu(domRect(200, 50, 100, 100))
    const insideTolerance = vi.fn()
    const outsideTolerance = vi.fn()
    const { result } = renderHook(() => useMenuAim({ submenuRef: { current: submenu }, side: 'right' }))

    act(() => {
      result.current.track({ clientX: 100, clientY: 100 })
      result.current.guard({ clientX: 203, clientY: 100 }, insideTolerance)
    })
    expect(insideTolerance).not.toHaveBeenCalled()

    act(() => result.current.guard({ clientX: 205, clientY: 100 }, outsideTolerance))
    expect(outsideTolerance).toHaveBeenCalledOnce()
  })

  it('reads the current submenu geometry for every guarded movement', () => {
    let rect = domRect(200, 50, 100, 100)
    const submenu = document.createElement('div')
    vi.spyOn(submenu, 'getBoundingClientRect').mockImplementation(() => rect)
    const action = vi.fn()
    const { result } = renderHook(() => useMenuAim({ submenuRef: { current: submenu }, side: 'right' }))

    act(() => {
      result.current.track({ clientX: 100, clientY: 100 })
      result.current.guard({ clientX: 150, clientY: 125 }, action)
    })
    expect(action).not.toHaveBeenCalled()

    rect = domRect(200, 300, 100, 100)
    act(() => result.current.guard({ clientX: 150, clientY: 125 }, action))

    expect(action).toHaveBeenCalledOnce()
    expect(vi.getTimerCount()).toBe(0)
  })

  it('refreshes the delay while pointer movement stays inside the cone', () => {
    const submenu = createSubmenu(domRect(200, 50, 100, 100))
    const action = vi.fn()
    const { result } = renderHook(() => useMenuAim({ submenuRef: { current: submenu }, side: 'right' }))

    act(() => {
      result.current.track({ clientX: 100, clientY: 100 })
      result.current.guard({ clientX: 140, clientY: 115 }, action)
      vi.advanceTimersByTime(200)
      result.current.guard({ clientX: 170, clientY: 125 }, action)
      vi.advanceTimersByTime(279)
    })
    expect(action).not.toHaveBeenCalled()

    act(() => vi.advanceTimersByTime(1))
    expect(action).toHaveBeenCalledOnce()
  })

  it('skips a delayed action when the pointer has entered the submenu', () => {
    const submenu = createSubmenu(domRect(200, 50, 100, 100))
    vi.spyOn(submenu, 'matches').mockImplementation((selector) => selector === ':hover')
    const action = vi.fn()
    const { result } = renderHook(() => useMenuAim({ submenuRef: { current: submenu }, side: 'right' }))

    act(() => {
      result.current.track({ clientX: 100, clientY: 100 })
      result.current.guard({ clientX: 150, clientY: 125 }, action)
      vi.advanceTimersByTime(280)
    })

    expect(action).not.toHaveBeenCalled()
  })

  it('cancels a pending action when its consumer unmounts', () => {
    const submenu = createSubmenu(domRect(200, 50, 100, 100))
    const action = vi.fn()
    const { result, unmount } = renderHook(() => useMenuAim({ submenuRef: { current: submenu }, side: 'right' }))

    act(() => {
      result.current.track({ clientX: 100, clientY: 100 })
      result.current.guard({ clientX: 150, clientY: 125 }, action)
    })
    unmount()
    act(() => vi.advanceTimersByTime(280))

    expect(action).not.toHaveBeenCalled()
  })
})

function createSubmenu(rect: DOMRect): HTMLDivElement {
  const submenu = document.createElement('div')
  vi.spyOn(submenu, 'getBoundingClientRect').mockReturnValue(rect)
  return submenu
}

function domRect(left: number, top: number, width: number, height: number): DOMRect {
  return {
    x: left,
    y: top,
    left,
    top,
    width,
    height,
    right: left + width,
    bottom: top + height,
    toJSON: () => ({})
  }
}

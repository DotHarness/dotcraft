// @vitest-environment jsdom

import { act, renderHook } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { useUsageRefresh } from '../components/settings/usage/useUsageRefresh'

afterEach(() => { vi.useRealTimers(); vi.restoreAllMocks() })

it('refreshes the mounted visible page and removes its timer and listeners on unmount', () => {
  vi.useFakeTimers()
  let visibility = 'visible'
  vi.spyOn(document, 'visibilityState', 'get').mockImplementation(() => visibility as DocumentVisibilityState)
  const refresh = vi.fn()
  const { unmount } = renderHook(() => useUsageRefresh(refresh))
  expect(refresh).toHaveBeenCalledTimes(1)
  act(() => { vi.advanceTimersByTime(60_000) })
  expect(refresh).toHaveBeenCalledTimes(2)
  visibility = 'hidden'
  act(() => { vi.advanceTimersByTime(60_000); window.dispatchEvent(new Event('focus')) })
  expect(refresh).toHaveBeenCalledTimes(2)
  visibility = 'visible'
  act(() => { document.dispatchEvent(new Event('visibilitychange')); window.dispatchEvent(new Event('focus')) })
  expect(refresh).toHaveBeenCalledTimes(4)
  unmount()
  act(() => { vi.advanceTimersByTime(60_000); window.dispatchEvent(new Event('focus')); document.dispatchEvent(new Event('visibilitychange')) })
  expect(refresh).toHaveBeenCalledTimes(4)
})

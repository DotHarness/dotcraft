import { act, fireEvent, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { closeSidebarPeek, useSidebarPeekDismiss, useSidebarPeekStore } from '../components/layout/sidebarPeek'

function renderOpenPeek(): void {
  const panel = document.createElement('div')
  document.body.appendChild(panel)
  useSidebarPeekStore.setState({ open: true })
  renderHook(() => useSidebarPeekDismiss(useSidebarPeekStore((s) => s.open), { current: panel }))
}

describe('sidebar peek dismissal', () => {
  beforeEach(() => vi.useFakeTimers())

  afterEach(() => {
    closeSidebarPeek()
    document.body.innerHTML = ''
    vi.useRealTimers()
  })

  it('stays open within the sidebar width and closes once the pointer moves past it', () => {
    renderOpenPeek()

    fireEvent.pointerMove(document, { clientX: 400 })
    fireEvent.pointerMove(document, { clientX: 4 })
    act(() => vi.advanceTimersByTime(500))
    expect(useSidebarPeekStore.getState().open).toBe(true)

    fireEvent.pointerMove(document, { clientX: 400 })
    act(() => vi.advanceTimersByTime(500))
    expect(useSidebarPeekStore.getState().open).toBe(false)
  })

  it('closes on Escape unless a menu opened from it is showing', () => {
    renderOpenPeek()
    const menu = document.createElement('div')
    menu.setAttribute('role', 'menu')
    document.body.appendChild(menu)

    act(() => { fireEvent.keyDown(document, { key: 'Escape' }) })
    expect(useSidebarPeekStore.getState().open).toBe(true)

    menu.remove()
    act(() => { fireEvent.keyDown(document, { key: 'Escape' }) })
    expect(useSidebarPeekStore.getState().open).toBe(false)
  })
})

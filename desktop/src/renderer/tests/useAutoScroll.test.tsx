import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { useAutoScroll } from '../hooks/useAutoScroll'

let resizeObserverCallback: ResizeObserverCallback | null = null

class ResizeObserverMock {
  constructor(callback: ResizeObserverCallback) {
    resizeObserverCallback = callback
  }

  observe(): void {}
  disconnect(): void {}
}

function setScrollMetrics(el: HTMLElement, clientHeight: number, scrollHeight: number): void {
  Object.defineProperty(el, 'clientHeight', { configurable: true, value: clientHeight })
  Object.defineProperty(el, 'scrollHeight', { configurable: true, value: scrollHeight })
}

function AutoScrollHarness({ contentLength }: { contentLength: number }): JSX.Element {
  const { scrollRef, showScrollButton } = useAutoScroll(contentLength)
  return (
    <div ref={scrollRef} data-testid="scroll-container">
      <div data-testid="scroll-content">content</div>
      {showScrollButton && <button type="button">Scroll to bottom</button>}
    </div>
  )
}

describe('useAutoScroll', () => {
  beforeEach(() => {
    resizeObserverCallback = null
    Object.defineProperty(window, 'ResizeObserver', {
      configurable: true,
      value: ResizeObserverMock
    })
    Object.defineProperty(globalThis, 'ResizeObserver', {
      configurable: true,
      value: ResizeObserverMock
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('continues following bottom when rendered content height changes without a contentLength change', () => {
    render(<AutoScrollHarness contentLength={1} />)

    const container = screen.getByTestId('scroll-container')
    setScrollMetrics(container, 100, 200)
    container.scrollTop = 100

    act(() => {
      fireEvent.scroll(container)
    })

    expect(screen.queryByRole('button', { name: 'Scroll to bottom' })).toBeNull()

    setScrollMetrics(container, 100, 320)

    act(() => {
      resizeObserverCallback?.([], {} as ResizeObserver)
    })

    expect(container.scrollTop).toBe(220)
  })

  it('keeps following the bottom when a stale scroll event arrives after content grew', () => {
    render(<AutoScrollHarness contentLength={1} />)

    const container = screen.getByTestId('scroll-container')

    // Start pinned to the bottom.
    setScrollMetrics(container, 100, 200)
    container.scrollTop = 100
    act(() => {
      fireEvent.scroll(container)
    })
    expect(screen.queryByRole('button', { name: 'Scroll to bottom' })).toBeNull()

    // Content grows; auto-scroll catches the new bottom programmatically.
    setScrollMetrics(container, 100, 400)
    act(() => {
      resizeObserverCallback?.([], {} as ResizeObserver)
    })
    expect(container.scrollTop).toBe(300)

    // More content streams in before the programmatic scroll's echoed `scroll`
    // event is delivered, so scrollTop now trails a taller scrollHeight.
    setScrollMetrics(container, 100, 600)
    act(() => {
      fireEvent.scroll(container)
    })

    // That stale echo must NOT disable auto-scroll.
    expect(screen.queryByRole('button', { name: 'Scroll to bottom' })).toBeNull()
  })

  it('disables auto-scroll when the user genuinely scrolls up', () => {
    render(<AutoScrollHarness contentLength={1} />)

    const container = screen.getByTestId('scroll-container')

    setScrollMetrics(container, 100, 500)
    container.scrollTop = 400
    act(() => {
      fireEvent.scroll(container)
    })
    expect(screen.queryByRole('button', { name: 'Scroll to bottom' })).toBeNull()

    // User drags the scrollbar up, away from the bottom.
    container.scrollTop = 100
    act(() => {
      fireEvent.scroll(container)
    })
    expect(screen.getByRole('button', { name: 'Scroll to bottom' })).not.toBeNull()
  })
})

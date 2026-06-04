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
})

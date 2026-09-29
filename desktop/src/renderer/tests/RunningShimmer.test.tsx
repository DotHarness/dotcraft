// @vitest-environment jsdom
import { act, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RunningShimmer } from '../components/ui/RunningShimmer'

describe('RunningShimmer', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    vi.stubGlobal('matchMedia', vi.fn(() => ({ matches: false })))
  })

  afterEach(() => {
    vi.unstubAllGlobals()
    document.documentElement.removeAttribute('data-reduce-motion')
  })

  it('keeps the highlight copy out of the accessible name', () => {
    render(<button type="button"><RunningShimmer>Running tests</RunningShimmer></button>)
    expect(screen.getByRole('button', { name: 'Running tests' })).toBeInTheDocument()
  })

  it('sweeps once per cadence and stops while the app reduces motion', () => {
    const { container } = render(<RunningShimmer>Running tests</RunningShimmer>)
    const root = container.querySelector('[data-running-shimmer]')!
    expect(root).not.toHaveAttribute('data-sweep')

    act(() => { vi.advanceTimersByTime(600) })
    expect(root).toHaveAttribute('data-sweep')
    act(() => { vi.advanceTimersByTime(1_000) })
    expect(root).not.toHaveAttribute('data-sweep')

    document.documentElement.setAttribute('data-reduce-motion', 'on')
    act(() => { vi.advanceTimersByTime(3_500) })
    expect(root).not.toHaveAttribute('data-sweep')
  })
})

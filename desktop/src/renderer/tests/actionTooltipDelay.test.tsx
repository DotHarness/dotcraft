import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { ActionTooltip } from '../components/ui/ActionTooltip'
import { resetTooltipWarmupForTests, TOOLTIP_OPEN_DELAY_MS } from '../components/ui/tooltipWarmup'

function renderPair() {
  render(
    <>
      <ActionTooltip label="First"><button type="button">one</button></ActionTooltip>
      <ActionTooltip label="Second"><button type="button">two</button></ActionTooltip>
    </>
  )
  return {
    first: screen.getByRole('button', { name: 'one' }).parentElement!,
    second: screen.getByRole('button', { name: 'two' }).parentElement!
  }
}

describe('ActionTooltip open delay', () => {
  beforeEach(() => {
    vi.useFakeTimers()
    resetTooltipWarmupForTests()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('waits for the hover delay and cancels when the pointer leaves first', () => {
    const { first } = renderPair()

    fireEvent.mouseEnter(first)
    act(() => { vi.advanceTimersByTime(TOOLTIP_OPEN_DELAY_MS - 1) })
    expect(screen.queryByRole('tooltip')).toBeNull()
    fireEvent.mouseLeave(first)
    act(() => { vi.advanceTimersByTime(TOOLTIP_OPEN_DELAY_MS) })
    expect(screen.queryByRole('tooltip')).toBeNull()

    fireEvent.mouseEnter(first)
    act(() => { vi.advanceTimersByTime(TOOLTIP_OPEN_DELAY_MS) })
    expect(screen.getByRole('tooltip', { name: 'First' })).toBeTruthy()
  })

  it('opens a neighbour at once right after another tooltip closes, then cools down', () => {
    const { first, second } = renderPair()

    fireEvent.mouseEnter(first)
    act(() => { vi.advanceTimersByTime(TOOLTIP_OPEN_DELAY_MS) })
    fireEvent.mouseLeave(first)
    fireEvent.mouseEnter(second)
    expect(screen.getByRole('tooltip', { name: 'Second' })).toBeTruthy()

    fireEvent.mouseLeave(second)
    act(() => { vi.advanceTimersByTime(1000) })
    fireEvent.mouseEnter(first)
    expect(screen.queryByRole('tooltip')).toBeNull()
  })

  it('shows at once on keyboard focus', () => {
    renderPair()
    fireEvent.focus(screen.getByRole('button', { name: 'one' }))
    expect(screen.getByRole('tooltip', { name: 'First' })).toBeTruthy()
  })
})

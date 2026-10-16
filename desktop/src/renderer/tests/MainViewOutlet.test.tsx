import { useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { MainViewOutlet, RETAIN_HIDDEN_VIEW_MS } from '../components/layout/MainViewOutlet'

function Counter({ label }: { label: string }): JSX.Element {
  const [count, setCount] = useState(0)
  return <button type="button" onClick={() => setCount((value) => value + 1)}>{label} {count}</button>
}

const renderView = (view: string): JSX.Element => <Counter label={view} />
const retains = (view: string): boolean => view !== 'settings'

function outlet(activeView: string): JSX.Element {
  return <MainViewOutlet activeView={activeView} retains={retains} renderView={renderView} />
}

describe('MainViewOutlet', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('keeps a view the user left mounted, so returning shows its previous state', () => {
    const { rerender } = render(outlet('conversation'))
    fireEvent.click(screen.getByRole('button', { name: 'conversation 0' }))
    rerender(outlet('skills'))
    expect(screen.queryByRole('button', { name: 'conversation 1' })).toBeNull()
    rerender(outlet('conversation'))
    expect(screen.getByRole('button', { name: 'conversation 1' })).toBeInTheDocument()
  })

  it('rebuilds a view it does not retain on every visit', () => {
    const { rerender } = render(outlet('settings'))
    fireEvent.click(screen.getByRole('button', { name: 'settings 0' }))
    rerender(outlet('conversation'))
    rerender(outlet('settings'))
    expect(screen.getByRole('button', { name: 'settings 0' })).toBeInTheDocument()
  })

  it('releases a hidden view once it has been hidden for the retention period', () => {
    vi.useFakeTimers()
    const { rerender } = render(outlet('skills'))
    fireEvent.click(screen.getByRole('button', { name: 'skills 0' }))
    rerender(outlet('conversation'))
    act(() => { vi.advanceTimersByTime(RETAIN_HIDDEN_VIEW_MS) })
    rerender(outlet('skills'))
    expect(screen.getByRole('button', { name: 'skills 0' })).toBeInTheDocument()
  })
})

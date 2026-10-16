import { useEffect, useRef, useState } from 'react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { MainViewOutlet, RETAIN_HIDDEN_VIEW_MS } from '../components/layout/MainViewOutlet'
import { retainsMainView } from '../core/coreMainViewRoutes'
import { useThreadStore } from '../stores/threadStore'

function Counter({ label }: { label: string }): JSX.Element {
  const [count, setCount] = useState(0)
  return <button type="button" onClick={() => setCount((value) => value + 1)}>{label} {count}</button>
}

// Claims the active thread when started and hands it back on unmount, like the Agent Builder.
function BuilderStandIn(): JSX.Element {
  const [threadId, setThreadId] = useState<string | null>(null)
  const previous = useRef<string | null>(null)
  const claimed = useRef<string | null>(null)
  useEffect(() => () => {
    if (claimed.current && useThreadStore.getState().activeThreadId === claimed.current) {
      useThreadStore.getState().setActiveThreadId(previous.current)
    }
  }, [])
  const start = (): void => {
    previous.current = useThreadStore.getState().activeThreadId
    claimed.current = 'builder-thread'
    useThreadStore.getState().setActiveThreadId('builder-thread')
    setThreadId('builder-thread')
  }
  return <button type="button" onClick={start}>builder {threadId ?? 'idle'}</button>
}

const renderView = (view: string): JSX.Element => view === 'agents' ? <BuilderStandIn /> : <Counter label={view} />

function outlet(activeView: string): JSX.Element {
  return <MainViewOutlet activeView={activeView} retains={retainsMainView} renderView={renderView} />
}

describe('MainViewOutlet', () => {
  afterEach(() => {
    vi.useRealTimers()
    useThreadStore.getState().setActiveThreadId(null)
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

  it('never shows an Agent Builder chat on top of the thread it handed back', () => {
    useThreadStore.getState().setActiveThreadId('normal-thread')
    const { rerender } = render(outlet('agents'))
    fireEvent.click(screen.getByRole('button', { name: 'builder idle' }))
    expect(useThreadStore.getState().activeThreadId).toBe('builder-thread')
    rerender(outlet('conversation'))
    expect(useThreadStore.getState().activeThreadId).toBe('normal-thread')
    rerender(outlet('agents'))
    expect(screen.getByRole('button', { name: 'builder idle' })).toBeInTheDocument()
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

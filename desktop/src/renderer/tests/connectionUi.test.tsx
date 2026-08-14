import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  isFatalConnectionError,
  STARTUP_SLOW_CONNECTING_HINT_MS,
  useSlowConnectingHint
} from '../utils/connectionUi'
import type { ConnectionStatus } from '../stores/connectionStore'

function SlowHintHost({
  status,
  workspacePath
}: {
  status: ConnectionStatus
  workspacePath: string
}): JSX.Element {
  const show = useSlowConnectingHint(status, workspacePath)
  return <div data-testid="slow-hint">{show ? 'visible' : 'hidden'}</div>
}

afterEach(() => {
  vi.useRealTimers()
})

describe('connection UI helpers', () => {
  it('keeps binary-not-found fatal but no longer treats handshake-timeout as fatal', () => {
    expect(isFatalConnectionError('error', 'binary-not-found')).toBe(true)
    expect(isFatalConnectionError('error', 'remote-config-invalid')).toBe(true)
    expect(isFatalConnectionError('error', 'handshake-timeout')).toBe(false)
    expect(isFatalConnectionError('connecting', null)).toBe(false)
  })

  it('shows the slow connecting hint after 15 seconds', () => {
    vi.useFakeTimers()
    expect(STARTUP_SLOW_CONNECTING_HINT_MS).toBe(15_000)

    render(<SlowHintHost status="connecting" workspacePath="X:\\fixtures\\workspace" />)

    expect(screen.getByTestId('slow-hint')).toHaveTextContent('hidden')

    act(() => {
      vi.advanceTimersByTime(STARTUP_SLOW_CONNECTING_HINT_MS - 1)
    })
    expect(screen.getByTestId('slow-hint')).toHaveTextContent('hidden')

    act(() => {
      vi.advanceTimersByTime(1)
    })
    expect(screen.getByTestId('slow-hint')).toHaveTextContent('visible')
  })

  it('hides the slow connecting hint outside workspace connecting state', () => {
    vi.useFakeTimers()

    const { rerender } = render(<SlowHintHost status="connecting" workspacePath="X:\\fixtures\\workspace" />)
    act(() => {
      vi.advanceTimersByTime(STARTUP_SLOW_CONNECTING_HINT_MS)
    })
    expect(screen.getByTestId('slow-hint')).toHaveTextContent('visible')

    rerender(<SlowHintHost status="connected" workspacePath="X:\\fixtures\\workspace" />)
    expect(screen.getByTestId('slow-hint')).toHaveTextContent('hidden')

    rerender(<SlowHintHost status="connecting" workspacePath="" />)
    expect(screen.getByTestId('slow-hint')).toHaveTextContent('hidden')
  })
})

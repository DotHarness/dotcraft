import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useWorkflowElapsed } from '../components/workflow/workflowPresentation'

function ElapsedFixture({ start, end }: { start?: string; end?: string }): JSX.Element {
  return <span>{useWorkflowElapsed(start, end)}</span>
}

describe('workflow elapsed presentation', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('advances a running workflow locally once per second', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-12T11:08:20.000Z'))
    render(<ElapsedFixture start="2026-08-12T11:08:15.000Z" />)

    expect(screen.getByText('5s')).toBeInTheDocument()
    act(() => {
      vi.advanceTimersByTime(1_000)
    })
    expect(screen.getByText('6s')).toBeInTheDocument()
  })

  it('keeps terminal elapsed time fixed', () => {
    vi.useFakeTimers()
    vi.setSystemTime(new Date('2026-08-12T11:10:00.000Z'))
    render(
      <ElapsedFixture
        start="2026-08-12T11:08:15.000Z"
        end="2026-08-12T11:08:42.000Z"
      />
    )

    expect(screen.getByText('27s')).toBeInTheDocument()
    act(() => {
      vi.advanceTimersByTime(5_000)
    })
    expect(screen.getByText('27s')).toBeInTheDocument()
  })
})

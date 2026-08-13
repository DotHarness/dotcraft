import { act, render, screen } from '@testing-library/react'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { formatWorkflowPhaseMetrics, useWorkflowElapsed } from '../components/workflow/workflowPresentation'

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

describe('workflow phase metrics', () => {
  it('aggregates token and tool cost and uses the completed phase wall-clock duration', () => {
    expect(formatWorkflowPhaseMetrics({
      name: 'Review',
      status: 'completed',
      agents: [
        {
          operationId: 'a', label: 'A', status: 'completed', replayed: false,
          requestedAt: '2026-08-12T11:08:00.000Z', startedAt: '2026-08-12T11:08:02.000Z',
          completedAt: '2026-08-12T11:08:29.000Z', inputTokens: 12_000, outputTokens: 3_000, toolCallCount: 4
        },
        {
          operationId: 'b', label: 'B', status: 'completed', replayed: false,
          requestedAt: '2026-08-12T11:08:01.000Z', startedAt: '2026-08-12T11:08:03.000Z',
          completedAt: '2026-08-12T11:08:34.000Z', inputTokens: 20_000, outputTokens: 5_000, toolCallCount: 6
        }
      ]
    })).toBe('40k tok · 10 tools · 32s')
  })

  it('omits elapsed time until the phase is complete', () => {
    expect(formatWorkflowPhaseMetrics({
      name: 'Review',
      status: 'running',
      agents: [{
        operationId: 'a', label: 'A', status: 'running', replayed: false,
        requestedAt: '2026-08-12T11:08:00.000Z', startedAt: '2026-08-12T11:08:02.000Z',
        inputTokens: 2_000, outputTokens: 500, toolCallCount: 3
      }]
    })).toBe('2.50k tok · 3 tools')
  })
})

// @vitest-environment jsdom
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fireEvent, render, screen } from '@testing-library/react'
import type { WorkflowRunView } from '@dotcraft/sdk/contracts'
import { LocaleProvider } from '../contexts/LocaleContext'
import { WorkflowViewerTab } from '../components/detail/viewers/WorkflowViewerTab'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { useWorkflowRunStore } from '../stores/workflowRunStore'

const run: WorkflowRunView = {
  runId: 'run-1',
  threadId: 'thread-1',
  name: 'release-readiness-review',
  description: 'Review release readiness.',
  status: 'running',
  createdAt: '2026-08-12T11:07:59.000Z',
  startedAt: '2026-08-12T11:08:00.000Z',
  controls: { canPause: false, canResume: false, canStop: true },
  totals: {
    agentCount: 2, completedCount: 1, failedCount: 0, inputTokens: 32_000,
    outputTokens: 8_000, queuedCount: 0, replayedCount: 0, runningCount: 1,
    stoppedCount: 0, toolCallCount: 10
  },
  phases: [
    {
      name: 'Inspect', detail: 'Map the implementation', status: 'completed',
      agents: [{
        operationId: 'inspect', label: 'Repository inventory', status: 'completed', replayed: false,
        requestedAt: '2026-08-12T11:08:00.000Z', startedAt: '2026-08-12T11:08:02.000Z',
        completedAt: '2026-08-12T11:08:20.000Z', inputTokens: 12_000, outputTokens: 3_000,
        toolCallCount: 4, childThreadId: 'child-inspect'
      }]
    },
    {
      name: 'Review', detail: 'Independent correctness reviews', status: 'running',
      agents: [{
        operationId: 'review', label: 'Runtime correctness', status: 'running', replayed: false,
        requestedAt: '2026-08-12T11:08:21.000Z', startedAt: '2026-08-12T11:08:22.000Z',
        inputTokens: 20_000, outputTokens: 5_000, toolCallCount: 6
      }]
    }
  ],
  unphasedAgents: []
}

beforeEach(() => {
  Object.defineProperty(window, 'api', {
    configurable: true,
    value: {
      settings: { get: vi.fn().mockResolvedValue({ locale: 'en' }) },
      appServer: {
        sendRequest: vi.fn().mockResolvedValue({ run }),
        onNotification: vi.fn(),
        onConnectionStatus: vi.fn()
      }
    }
  })
  useViewerTabStore.setState({
    byThread: new Map(), currentThreadId: 'thread-1', currentWorkspacePath: null
  })
  useWorkflowRunStore.setState({ entries: new Map() })
})

describe('WorkflowViewerTab', () => {
  it('collapses phases into aggregate cost and exposes a neutral icon-only stop action', async () => {
    const tabId = useViewerTabStore.getState().openWorkflow({
      threadId: 'thread-1', runId: run.runId, initialLabel: run.name
    })
    render(<LocaleProvider><WorkflowViewerTab tabId={tabId} /></LocaleProvider>)

    await screen.findByRole('heading', { name: run.name })
    const inspect = screen.getByText('Inspect').closest('button')!
    const review = screen.getByText('Review').closest('button')!
    expect(inspect).toHaveAttribute('aria-expanded', 'false')
    expect(inspect).toHaveTextContent('15k tok · 4 tools · 18s')
    expect(review).toHaveAttribute('aria-expanded', 'true')

    fireEvent.click(review)
    expect(review).toHaveAttribute('aria-expanded', 'false')
    expect(review).toHaveTextContent('25k tok · 6 tools')

    const stop = screen.getByRole('button', { name: 'Stop' })
    expect(stop).toHaveTextContent('')
    expect(stop).toHaveAttribute('data-tone', 'neutral')
  })
})

import { beforeEach, describe, expect, it, vi } from 'vitest'

const sendRequest = vi.fn()
let notificationHandler: ((payload: { method: string; params: unknown }) => void) | undefined
let connectionHandler: (() => void) | undefined

vi.stubGlobal('window', {
  api: {
    appServer: {
      sendRequest,
      onNotification: (handler: typeof notificationHandler) => { notificationHandler = handler },
      onConnectionStatus: (handler: typeof connectionHandler) => { connectionHandler = handler }
    }
  }
})

const { selectWorkflowRunEntry, useWorkflowRunStore } = await import('../stores/workflowRunStore')

const run = {
  runId: 'run-1',
  threadId: 'thread-1',
  name: 'release-review',
  description: 'Review the release.',
  status: 'running',
  controls: { canPause: true, canStop: true, canResume: false },
  totals: { requestedAgents: 1, runningAgents: 1, completedAgents: 0, failedAgents: 0, cancelledAgents: 0, inputTokens: 0, outputTokens: 0, toolCallCount: 0 },
  phases: [],
  unphasedAgents: [],
  createdAt: '2026-08-12T00:00:00Z'
}

describe('workflowRunStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useWorkflowRunStore.setState({ entries: new Map() })
  })

  it('reads a run and refreshes it after an invalidation notification', async () => {
    sendRequest.mockResolvedValue({ run })

    await useWorkflowRunStore.getState().load('thread-1', 'run-1')
    expect(selectWorkflowRunEntry(useWorkflowRunStore.getState().entries, 'thread-1', 'run-1')?.run).toEqual(run)

    notificationHandler?.({
      method: 'workflow/run/updated',
      params: { threadId: 'thread-1', runId: 'run-1', reason: 'progress' }
    })
    await vi.waitFor(() => expect(sendRequest).toHaveBeenCalledTimes(2))
    expect(sendRequest).toHaveBeenLastCalledWith('workflow/run/read', { threadId: 'thread-1', runId: 'run-1' })
  })

  it('re-reads cached runs after reconnect and stores the stop projection', async () => {
    sendRequest.mockResolvedValueOnce({ run })
    await useWorkflowRunStore.getState().load('thread-1', 'run-1')

    connectionHandler?.()
    await vi.waitFor(() => expect(sendRequest).toHaveBeenCalledTimes(2))

    const stoppedRun = { ...run, status: 'stopped', controls: { canPause: false, canStop: false, canResume: true } }
    sendRequest.mockResolvedValueOnce({ run: stoppedRun })
    await useWorkflowRunStore.getState().stop('thread-1', 'run-1')

    expect(sendRequest).toHaveBeenLastCalledWith('workflow/run/stop', { threadId: 'thread-1', runId: 'run-1' })
    expect(selectWorkflowRunEntry(useWorkflowRunStore.getState().entries, 'thread-1', 'run-1')?.run).toEqual(stoppedRun)
  })
})

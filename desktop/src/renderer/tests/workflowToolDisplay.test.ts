import { setRendererTestState } from './panelTestState'
import { describe, expect, it } from 'vitest'
import { autoOpenWorkflowLaunch, parseWorkflowLaunch, parseWorkflowRunId } from '../components/workflow/WorkflowToolCard'
import { useDetailPanelStore } from '../stores/detailPanelStore'

describe('parseWorkflowRunId', () => {
  it('recognizes only a successful Workflow tool result and only a running launch for auto-open', () => {
    expect(parseWorkflowRunId('Workflow', '{"runId":"run_001","status":"running"}')).toBe('run_001')
    expect(parseWorkflowRunId('shell', '{"runId":"run_001"}')).toBeNull()
    expect(parseWorkflowRunId('Workflow', 'not json')).toBeNull()
    expect(parseWorkflowLaunch('Workflow', '{"runId":"run_001","name":"review","status":"running"}'))
      .toEqual({ runId: 'run_001', name: 'review' })
    expect(parseWorkflowLaunch('Workflow', '{"runId":"run_001","name":"review","status":"failed"}')).toBeNull()
    expect(parseWorkflowLaunch('Workflow', '{"error":"invalid"}')).toBeNull()
  })

  it('auto-opens each successful run only once', () => {
    setRendererTestState({ autoShowReasons: new Set(), activeDetailTab: { kind: 'system', id: 'changes' } })
    useDetailPanelStore.setState({ byThread: new Map(), currentThreadId: 'thread-1', currentWorkspacePath: null })
    const result = '{"runId":"run_001","name":"review","status":"running"}'

    expect(autoOpenWorkflowLaunch('thread-1', 'Workflow', result, true)).toBe(true)
    expect(autoOpenWorkflowLaunch('thread-1', 'Workflow', result, true)).toBe(false)
    const threadState = useDetailPanelStore.getState().getThreadState('thread-1')
    expect(threadState.tabs).toHaveLength(1)
    expect(threadState.tabs[0]).toEqual(expect.objectContaining({ kind: 'workflow', runId: 'run_001' }))
  })
})

import { describe, expect, it } from 'vitest'
import { autoOpenWorkflowLaunch, parseWorkflowLaunch, parseWorkflowRunId } from '../components/workflow/WorkflowToolCard'
import { formatCollapsedToolLabel, formatWorkflowFailureLabel, getStreamingToolDisplay } from '../utils/toolCallDisplay'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'

describe('parseWorkflowRunId', () => {
  it('recognizes only a successful Workflow tool result', () => {
    expect(parseWorkflowRunId('Workflow', '{"runId":"run_001","status":"running"}')).toBe('run_001')
    expect(parseWorkflowRunId('shell', '{"runId":"run_001"}')).toBeNull()
    expect(parseWorkflowRunId('Workflow', 'not json')).toBeNull()
  })

  it('uses dedicated Workflow lifecycle labels', () => {
    expect(getStreamingToolDisplay('Workflow', '', 'en').label).toBe('Preparing workflow…')
    expect(getStreamingToolDisplay('Workflow', '{"name":"release-review', 'en').label)
      .toBe('Starting workflow release-review…')
    expect(formatCollapsedToolLabel('Workflow', { name: 'release-review' }, 'en'))
      .toBe('Started workflow release-review')
    expect(formatWorkflowFailureLabel({ name: 'release-review' }, 'en'))
      .toBe('Could not start workflow release-review')
  })

  it('parses only a successful running launch for auto-open', () => {
    expect(parseWorkflowLaunch('Workflow', '{"runId":"run_001","name":"review","status":"running"}'))
      .toEqual({ runId: 'run_001', name: 'review' })
    expect(parseWorkflowLaunch('Workflow', '{"runId":"run_001","name":"review","status":"failed"}')).toBeNull()
    expect(parseWorkflowLaunch('Workflow', '{"error":"invalid"}')).toBeNull()
  })

  it('auto-opens each successful run only once', () => {
    useUIStore.setState({ autoShowReasons: new Set(), activeDetailTab: { kind: 'system', id: 'changes' } })
    useViewerTabStore.setState({ byThread: new Map(), currentThreadId: 'thread-1', currentWorkspacePath: null })
    const result = '{"runId":"run_001","name":"review","status":"running"}'

    expect(autoOpenWorkflowLaunch('thread-1', 'Workflow', result, true)).toBe(true)
    expect(autoOpenWorkflowLaunch('thread-1', 'Workflow', result, true)).toBe(false)
    const threadState = useViewerTabStore.getState().getThreadState('thread-1')
    expect(threadState.tabs).toHaveLength(1)
    expect(threadState.tabs[0]).toEqual(expect.objectContaining({ kind: 'workflow', runId: 'run_001' }))
  })
})

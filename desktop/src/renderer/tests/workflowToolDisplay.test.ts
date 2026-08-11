import { describe, expect, it } from 'vitest'
import { parseWorkflowRunId } from '../components/workflow/WorkflowToolCard'

describe('parseWorkflowRunId', () => {
  it('recognizes only a successful Workflow tool result', () => {
    expect(parseWorkflowRunId('Workflow', '{"runId":"run_001","status":"running"}')).toBe('run_001')
    expect(parseWorkflowRunId('shell', '{"runId":"run_001"}')).toBeNull()
    expect(parseWorkflowRunId('Workflow', 'not json')).toBeNull()
  })
})

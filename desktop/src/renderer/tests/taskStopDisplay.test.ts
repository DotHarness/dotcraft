import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import { getTaskStopDisplay } from '../utils/taskStopDisplay'
import { coreToolFields } from './testToolPresentation'

function stop(overrides: Partial<ConversationItem> = {}): ConversationItem {
  return {
    id: 'stop', type: 'toolCall', toolName: 'RenamedStop', status: 'completed',
    ...coreToolFields('TaskStop'),
    presentation: { presentationId: 'core.task', options: { operation: 'stop', kind: 'agent' } },
    arguments: { taskId: 'review_core' },
    result: JSON.stringify({ taskId: '/root/review_core', kind: 'agent', label: 'Core', status: 'stopped' }),
    success: true, createdAt: '', ...overrides
  }
}

describe('TaskStop display data', () => {
  it('keeps the canonical target and returned stopping outcome', () => {
    const item = stop({ result: JSON.stringify({
      taskId: '/root/review_core', kind: 'agent', label: 'Core', status: 'stopping'
    }) })
    expect(getTaskStopDisplay(item, false)).toEqual({
      kind: 'agent', taskId: '/root/review_core', label: 'Core', stopping: true
    })
  })

  it.each([
    { source: { kind: 'Mcp', sourceId: 'fixture' } },
    { success: false, result: 'Stop failed' }
  ] satisfies Partial<ConversationItem>[])('leaves ineligible or failed calls to their existing renderer: %o', (overrides) => {
    expect(getTaskStopDisplay(stop(overrides), false)).toBeNull()
  })
})

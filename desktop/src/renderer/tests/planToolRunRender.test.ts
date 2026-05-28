import { describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import { aggregateToolCalls, planToolRunRender } from '../utils/toolCallAggregation'

function makeItem(
  toolName: string,
  id: string,
  overrides: Partial<ConversationItem> = {}
): ConversationItem {
  return {
    id,
    type: 'toolCall',
    status: 'completed',
    toolName,
    toolCallId: id,
    createdAt: new Date().toISOString(),
    ...overrides
  }
}

describe('planToolRunRender', () => {
  it('keeps two-item trailing run as singles while the turn is still running', () => {
    const run = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2')
    ]

    const result = planToolRunRender(run, { isRunning: true, isTrailingRun: true })
    expect(result.entries).toHaveLength(2)
    expect(result.entries.every((entry) => entry.kind === 'single')).toBe(true)
  })

  it('keeps longer trailing completed runs as singles while the turn is still running', () => {
    const run = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2'),
      makeItem('EditFile', '3')
    ]

    const result = planToolRunRender(run, { isRunning: true, isTrailingRun: true })
    expect(result.entries).toEqual([
      { kind: 'single', item: run[0] },
      { kind: 'single', item: run[1] },
      { kind: 'single', item: run[2] }
    ])
  })

  it('keeps trailing run as singles even when the last item is live', () => {
    const run = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2'),
      makeItem('EditFile', '3', { status: 'streaming' })
    ]

    const result = planToolRunRender(run, { isRunning: true, isTrailingRun: true })
    expect(result.entries).toEqual([
      { kind: 'single', item: run[0] },
      { kind: 'single', item: run[1] },
      { kind: 'single', item: run[2] }
    ])
  })

  it('aggregates non-trailing run while still running', () => {
    const run = [
      makeItem('ReadFile', '1', { result: 'ok', success: true }),
      makeItem('FindFiles', '2', { result: 'ok', success: true })
    ]

    const result = planToolRunRender(run, { isRunning: true, isTrailingRun: false })
    expect(result.entries).toEqual(aggregateToolCalls(run))
  })

  it('aggregates trailing run when turn is not running', () => {
    const run = [
      makeItem('Exec', '1', { result: 'ok', success: true }),
      makeItem('RunCommand', '2', { result: 'ok', success: true })
    ]

    const result = planToolRunRender(run, { isRunning: false, isTrailingRun: true })
    expect(result.entries).toEqual(aggregateToolCalls(run))
  })
})

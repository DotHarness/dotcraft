import { describe, it, expect } from 'vitest'
import { aggregateToolCalls, isToolItemLive, planToolRunRender } from '../utils/toolCallAggregation'
import type { ConversationItem } from '../types/conversation'

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

describe('aggregateToolCalls', () => {
  it('returns empty array for empty input', () => {
    expect(aggregateToolCalls([])).toHaveLength(0)
  })

  it('keeps a single ReadFile as individual card', () => {
    const items = [makeItem('ReadFile', '1')]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('single')
    if (result[0].kind === 'single') {
      expect(result[0].item.toolName).toBe('ReadFile')
    }
  })

  it('groups three consecutive ReadFile calls into one group', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('ReadFile', '2'),
      makeItem('ReadFile', '3')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].items).toHaveLength(3)
      expect(result[0].category).toBe('explore')
    }
  })

  it('groups consecutive explore tools into one group', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('GrepFiles', '2'),
      makeItem('FindFiles', '3')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].items).toHaveLength(3)
      expect(result[0].category).toBe('explore')
    }
  })

  it('groups consecutive write tools into one group', () => {
    const items = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('write')
      expect(result[0].items).toHaveLength(2)
    }
  })

  it('handles mixed sequences: [ReadFile, WriteFile, ReadFile, ReadFile]', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('WriteFile', '2'),
      makeItem('ReadFile', '3'),
      makeItem('ReadFile', '4')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    // First: single ReadFile
    expect(result[0].kind).toBe('single')
    if (result[0].kind === 'single') {
      expect(result[0].item.toolName).toBe('ReadFile')
    }
    // Second: single WriteFile
    expect(result[1].kind).toBe('single')
    if (result[1].kind === 'single') {
      expect(result[1].item.toolName).toBe('WriteFile')
    }
    // Third: group of 2 ReadFiles
    expect(result[2].kind).toBe('group')
    if (result[2].kind === 'group') {
      expect(result[2].category).toBe('explore')
    }
  })

  it('groups consecutive shell tools into one group', () => {
    const items = [
      makeItem('Exec', '1', { result: 'ok', success: true }),
      makeItem('RunCommand', '2', { result: 'ok', success: true }),
      makeItem('BashCommand', '3', { result: 'ok', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('shell')
      expect(result[0].items).toHaveLength(3)
    }
  })

  it('groups consecutive WebSearch calls into one web group', () => {
    const items = [
      makeItem('WebSearch', '1', { result: '{"results":[]}', success: true }),
      makeItem('WebSearch', '2', { result: '{"results":[]}', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('web')
      expect(result[0].items).toHaveLength(2)
    }
  })

  it('groups mixed WebSearch and WebFetch calls into one web group', () => {
    const items = [
      makeItem('WebSearch', '1', { result: '{"results":[]}', success: true }),
      makeItem('WebFetch', '2', { result: '{"status":200}', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('web')
      expect(result[0].items.map((item) => item.toolName)).toEqual(['WebSearch', 'WebFetch'])
    }
  })

  it('groups consecutive settled SpawnAgent calls', () => {
    const items = [
      makeItem('SpawnAgent', '1', { result: '{"status":"running"}', success: true }),
      makeItem('SpawnAgent', '2', { result: '{"status":"running"}', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('subagent')
      expect(result[0].items).toHaveLength(2)
    }
  })

  it('keeps running SpawnAgent calls as individual cards', () => {
    const items = [
      makeItem('SpawnAgent', '1', { status: 'started' }),
      makeItem('SpawnAgent', '2', { result: '{"status":"running"}', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(2)
    expect(result[0].kind).toBe('single')
    expect(result[1].kind).toBe('single')
  })

  it('preserves order of non-aggregatable items', () => {
    const items = [
      makeItem('Exec', '1'),
      makeItem('ReadFile', '2'),
      makeItem('GrepFiles', '3'),
      makeItem('WriteFile', '4')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    if (result[0].kind === 'single') expect(result[0].item.toolName).toBe('Exec')
    if (result[1].kind === 'group') expect(result[1].category).toBe('explore')
    if (result[2].kind === 'single') expect(result[2].item.toolName).toBe('WriteFile')
  })

  it('does not aggregate across category transitions', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('WriteFile', '2'),
      makeItem('Exec', '3')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    for (const entry of result) {
      expect(entry.kind).toBe('single')
    }
  })

  it('groups each category independently in a mixed run', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('FindFiles', '2'),
      makeItem('WriteFile', '3'),
      makeItem('EditFile', '4'),
      makeItem('Exec', '5', { result: 'ok', success: true }),
      makeItem('RunCommand', '6', { result: 'ok', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    expect(result[0].kind).toBe('group')
    expect(result[1].kind).toBe('group')
    expect(result[2].kind).toBe('group')
    if (result[0].kind === 'group') expect(result[0].category).toBe('explore')
    if (result[1].kind === 'group') expect(result[1].category).toBe('write')
    if (result[2].kind === 'group') expect(result[2].category).toBe('shell')
  })

  it('keeps settled write prefix grouped when trailing write item is live', () => {
    const items = [
      makeItem('WriteFile', '1'),
      makeItem('EditFile', '2'),
      makeItem('WriteFile', '3', { status: 'streaming' })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(2)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('write')
      expect(result[0].items.map((item) => item.id)).toEqual(['1', '2'])
    }
    expect(result[1].kind).toBe('single')
    if (result[1].kind === 'single') {
      expect(result[1].item.id).toBe('3')
    }
  })

  it('keeps settled shell prefix grouped when trailing shell execution is live', () => {
    const items = [
      makeItem('Exec', '1', {
        status: 'completed',
        executionStatus: 'completed',
        result: 'done',
        success: true
      }),
      makeItem('RunCommand', '2', {
        status: 'completed',
        executionStatus: 'completed',
        result: 'done',
        success: true
      }),
      makeItem('BashCommand', '3', {
        status: 'completed',
        executionStatus: 'inProgress'
      })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(2)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('shell')
      expect(result[0].items.map((item) => item.id)).toEqual(['1', '2'])
    }
    expect(result[1].kind).toBe('single')
    if (result[1].kind === 'single') {
      expect(result[1].item.id).toBe('3')
    }
  })

  it('does not de-aggregate settled prefix and suffix around a live item', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('FindFiles', '2'),
      makeItem('ReadFile', '3', { status: 'streaming' }),
      makeItem('GrepFiles', '4'),
      makeItem('ReadFile', '5')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].items.map((item) => item.id)).toEqual(['1', '2'])
    }
    expect(result[1].kind).toBe('single')
    if (result[1].kind === 'single') {
      expect(result[1].item.id).toBe('3')
    }
    expect(result[2].kind).toBe('group')
    if (result[2].kind === 'group') {
      expect(result[2].items.map((item) => item.id)).toEqual(['4', '5'])
    }
  })

  it('keeps settled web calls grouped around a live web item', () => {
    const items = [
      makeItem('WebSearch', '1', { result: '{"results":[]}', success: true }),
      makeItem('WebFetch', '2', { result: '{"status":200}', success: true }),
      makeItem('WebSearch', '3', { status: 'streaming' }),
      makeItem('WebSearch', '4', { result: '{"results":[]}', success: true }),
      makeItem('WebFetch', '5', { result: '{"status":200}', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('web')
      expect(result[0].items.map((item) => item.id)).toEqual(['1', '2'])
    }
    expect(result[1].kind).toBe('single')
    if (result[1].kind === 'single') {
      expect(result[1].item.id).toBe('3')
    }
    expect(result[2].kind).toBe('group')
    if (result[2].kind === 'group') {
      expect(result[2].category).toBe('web')
      expect(result[2].items.map((item) => item.id)).toEqual(['4', '5'])
    }
  })

  it('treats completed tool calls without tool results as live only while the turn is running', () => {
    const pending = makeItem('WaitAgent', 'wait-1', {
      arguments: { agentNickname: 'Reviewer' }
    })

    expect(isToolItemLive(pending)).toBe(false)
    expect(isToolItemLive(pending, { turnRunning: true })).toBe(true)
  })

  it('keeps pending-result tools out of settled groups in running turns', () => {
    const items = [
      makeItem('ReadFile', '1', { result: 'ok', success: true }),
      makeItem('ReadFile', '2'),
      makeItem('ReadFile', '3', { result: 'ok', success: true })
    ]

    const result = aggregateToolCalls(items, { turnRunning: true })

    expect(result).toHaveLength(3)
    expect(result[0].kind).toBe('single')
    expect(result[1].kind).toBe('single')
    expect(result[2].kind).toBe('single')
    if (result[1].kind === 'single') {
      expect(result[1].item.id).toBe('2')
    }
  })

  it('does not keep missing-result historical tools live after the turn is completed', () => {
    const items = [
      makeItem('ReadFile', '1'),
      makeItem('ReadFile', '2')
    ]

    const result = planToolRunRender(items, { isRunning: false, isTrailingRun: false })

    expect(result.entries).toHaveLength(1)
    expect(result.entries[0].kind).toBe('group')
  })
})

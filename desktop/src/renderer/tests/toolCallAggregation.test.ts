import { describe, it, expect } from 'vitest'
import { aggregateToolCalls, isToolItemLive, planToolRunRender } from '../utils/toolCallAggregation'
import type { ConversationItem } from '../types/conversation'
import { CORE_TOOL_PRESENTATION_IDS } from '../utils/toolRendererRegistry'
import { withTestCorePresentation } from './testToolPresentation'

interface CoreFixturePresentation {
  presentationId: string
  options?: Record<string, unknown>
}

const EXPLORE: CoreFixturePresentation = { presentationId: CORE_TOOL_PRESENTATION_IDS.readFile }
const WRITE: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.fileWrite,
  options: { operation: 'write' }
}
const EDIT: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.fileWrite,
  options: { operation: 'edit' }
}
const SHELL: CoreFixturePresentation = { presentationId: CORE_TOOL_PRESENTATION_IDS.shell }
const WEB_SEARCH: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.web,
  options: { operation: 'search' }
}
const WEB_FETCH: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.web,
  options: { operation: 'fetch' }
}
const SUBAGENT_SPAWN: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
  options: { operation: 'spawn' }
}
const SUBAGENT_WAIT: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
  options: { operation: 'wait' }
}
const SUBAGENT_MESSAGE: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
  options: { operation: 'sendMessage' }
}
const SUBAGENT_FOLLOWUP: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
  options: { operation: 'followupTask' }
}
const SUBAGENT_LIST: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
  options: { operation: 'list' }
}

function makeItem(
  presentation: CoreFixturePresentation,
  toolName: string,
  id: string,
  overrides: Partial<ConversationItem> = {}
): ConversationItem {
  return withTestCorePresentation({
    id,
    type: 'toolCall',
    status: 'completed',
    toolName,
    toolCallId: id,
    createdAt: new Date().toISOString(),
    ...overrides
  }, presentation.presentationId, presentation.options)
}

describe('aggregateToolCalls', () => {
  it('returns empty array for empty input', () => {
    expect(aggregateToolCalls([])).toHaveLength(0)
  })

  it('keeps a single ReadFile as individual card', () => {
    const items = [makeItem(EXPLORE, 'ReadFile', '1')]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('single')
    if (result[0].kind === 'single') {
      expect(result[0].item.toolName).toBe('ReadFile')
    }
  })

  it('groups three consecutive ReadFile calls into one group', () => {
    const items = [
      makeItem(EXPLORE, 'ReadFile', '1'),
      makeItem(EXPLORE, 'ReadFile', '2'),
      makeItem(EXPLORE, 'ReadFile', '3')
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
      makeItem(EXPLORE, 'ReadFile', '1'),
      makeItem(EXPLORE, 'GrepFiles', '2'),
      makeItem(EXPLORE, 'FindFiles', '3')
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
      makeItem(WRITE, 'WriteFile', '1'),
      makeItem(EDIT, 'EditFile', '2')
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
      makeItem(EXPLORE, 'ReadFile', '1'),
      makeItem(WRITE, 'WriteFile', '2'),
      makeItem(EXPLORE, 'ReadFile', '3'),
      makeItem(EXPLORE, 'ReadFile', '4')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    expect(result[0].kind).toBe('single')
    if (result[0].kind === 'single') {
      expect(result[0].item.toolName).toBe('ReadFile')
    }
    expect(result[1].kind).toBe('single')
    if (result[1].kind === 'single') {
      expect(result[1].item.toolName).toBe('WriteFile')
    }
    expect(result[2].kind).toBe('group')
    if (result[2].kind === 'group') {
      expect(result[2].category).toBe('explore')
    }
  })

  it('groups consecutive shell tools into one group', () => {
    const items = [
      makeItem(SHELL, 'Exec', '1', { result: 'ok', success: true }),
      makeItem(SHELL, 'RunCommand', '2', { result: 'ok', success: true }),
      makeItem(SHELL, 'BashCommand', '3', { result: 'ok', success: true })
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
      makeItem(WEB_SEARCH, 'WebSearch', '1', { result: '{"results":[]}', success: true }),
      makeItem(WEB_SEARCH, 'WebSearch', '2', { result: '{"results":[]}', success: true })
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
      makeItem(WEB_SEARCH, 'WebSearch', '1', { result: '{"results":[]}', success: true }),
      makeItem(WEB_FETCH, 'WebFetch', '2', { result: '{"status":200}', success: true })
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
      makeItem(SUBAGENT_SPAWN, 'SpawnAgent', '1', { result: '{"status":"running"}', success: true }),
      makeItem(SUBAGENT_SPAWN, 'SpawnAgent', '2', { result: '{"status":"running"}', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('subagent')
      expect(result[0].items).toHaveLength(2)
    }
  })

  it('keeps non-narrative SubAgent control calls out of aggregation', () => {
    const items = [
      makeItem(SUBAGENT_MESSAGE, 'SendMessage', '1', { result: '{"status":"sent"}', success: true }),
      makeItem(SUBAGENT_WAIT, 'WaitAgent', '2', { result: '{"status":"timeout"}', success: true }),
      makeItem(SUBAGENT_LIST, 'ListAgents', '3', { result: '{"data":[]}', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    expect(result.every((entry) => entry.kind === 'single')).toBe(true)
  })

  it('groups consecutive settled FollowupTask calls', () => {
    const items = [
      makeItem(SUBAGENT_FOLLOWUP, 'FollowupTask', '1', { result: '{"status":"running"}', success: true }),
      makeItem(SUBAGENT_FOLLOWUP, 'FollowupTask', '2', { result: '{"status":"running"}', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('subagent')
      expect(result[0].items.map((item) => item.toolName)).toEqual(['FollowupTask', 'FollowupTask'])
    }
  })

  it('does not combine SpawnAgent and FollowupTask calls', () => {
    const items = [
      makeItem(SUBAGENT_SPAWN, 'SpawnAgent', '1', { result: '{"status":"running"}', success: true }),
      makeItem(SUBAGENT_FOLLOWUP, 'FollowupTask', '2', { result: '{"status":"running"}', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(2)
    expect(result.every((entry) => entry.kind === 'single')).toBe(true)
  })

  it('keeps a starting SpawnAgent on the same line as its settled neighbour', () => {
    const items = [
      makeItem(SUBAGENT_SPAWN, 'SpawnAgent', '1', { status: 'started' }),
      makeItem(SUBAGENT_SPAWN, 'SpawnAgent', '2', { result: '{"status":"running"}', success: true })
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(1)
    expect(result[0].kind).toBe('group')
    if (result[0].kind === 'group') {
      expect(result[0].category).toBe('subagent')
      expect(result[0].items).toHaveLength(2)
    }
  })

  it('preserves order of non-aggregatable items', () => {
    const items = [
      makeItem(SHELL, 'Exec', '1'),
      makeItem(EXPLORE, 'ReadFile', '2'),
      makeItem(EXPLORE, 'GrepFiles', '3'),
      makeItem(WRITE, 'WriteFile', '4')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    if (result[0].kind === 'single') expect(result[0].item.toolName).toBe('Exec')
    if (result[1].kind === 'group') expect(result[1].category).toBe('explore')
    if (result[2].kind === 'single') expect(result[2].item.toolName).toBe('WriteFile')
  })

  it('does not aggregate across category transitions', () => {
    const items = [
      makeItem(EXPLORE, 'ReadFile', '1'),
      makeItem(WRITE, 'WriteFile', '2'),
      makeItem(SHELL, 'Exec', '3')
    ]
    const result = aggregateToolCalls(items)
    expect(result).toHaveLength(3)
    for (const entry of result) {
      expect(entry.kind).toBe('single')
    }
  })

  it('groups each category independently in a mixed run', () => {
    const items = [
      makeItem(EXPLORE, 'ReadFile', '1'),
      makeItem(EXPLORE, 'FindFiles', '2'),
      makeItem(WRITE, 'WriteFile', '3'),
      makeItem(EDIT, 'EditFile', '4'),
      makeItem(SHELL, 'Exec', '5', { result: 'ok', success: true }),
      makeItem(SHELL, 'RunCommand', '6', { result: 'ok', success: true })
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
      makeItem(WRITE, 'WriteFile', '1'),
      makeItem(EDIT, 'EditFile', '2'),
      makeItem(WRITE, 'WriteFile', '3', { status: 'streaming' })
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
      makeItem(SHELL, 'Exec', '1', {
        status: 'completed',
        executionStatus: 'completed',
        result: 'done',
        success: true
      }),
      makeItem(SHELL, 'RunCommand', '2', {
        status: 'completed',
        executionStatus: 'completed',
        result: 'done',
        success: true
      }),
      makeItem(SHELL, 'BashCommand', '3', {
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
      makeItem(EXPLORE, 'ReadFile', '1'),
      makeItem(EXPLORE, 'FindFiles', '2'),
      makeItem(EXPLORE, 'ReadFile', '3', { status: 'streaming' }),
      makeItem(EXPLORE, 'GrepFiles', '4'),
      makeItem(EXPLORE, 'ReadFile', '5')
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
      makeItem(WEB_SEARCH, 'WebSearch', '1', { result: '{"results":[]}', success: true }),
      makeItem(WEB_FETCH, 'WebFetch', '2', { result: '{"status":200}', success: true }),
      makeItem(WEB_SEARCH, 'WebSearch', '3', { status: 'streaming' }),
      makeItem(WEB_SEARCH, 'WebSearch', '4', { result: '{"results":[]}', success: true }),
      makeItem(WEB_FETCH, 'WebFetch', '5', { result: '{"status":200}', success: true })
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
    const pending = makeItem(SUBAGENT_WAIT, 'WaitAgent', 'wait-1', {
      arguments: { agentNickname: 'Reviewer' }
    })

    expect(isToolItemLive(pending)).toBe(false)
    expect(isToolItemLive(pending, { turnRunning: true })).toBe(true)
  })

  it('keeps pending-result tools out of settled groups in running turns', () => {
    const items = [
      makeItem(EXPLORE, 'ReadFile', '1', { result: 'ok', success: true }),
      makeItem(EXPLORE, 'ReadFile', '2'),
      makeItem(EXPLORE, 'ReadFile', '3', { result: 'ok', success: true })
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
      makeItem(EXPLORE, 'ReadFile', '1'),
      makeItem(EXPLORE, 'ReadFile', '2')
    ]

    const result = planToolRunRender(items, { isRunning: false, isTrailingRun: false })

    expect(result.entries).toHaveLength(1)
    expect(result.entries[0].kind).toBe('group')
  })
})

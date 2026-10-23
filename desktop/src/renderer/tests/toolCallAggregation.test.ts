import { describe, it, expect } from 'vitest'
import { isToolItemLive, planToolRunRender, resolveShellBackgroundState } from '../utils/toolCallAggregation'
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
const RECALL: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.readFile,
  options: { operation: 'read', memory: 'recall' }
}
const MEMORY_EDIT: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.fileWrite,
  options: { operation: 'edit', memory: 'update' }
}
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
const SUBAGENT_MESSAGE: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.subagent,
  options: { operation: 'sendMessage' }
}
const TASK_STOP: CoreFixturePresentation = {
  presentationId: CORE_TOOL_PRESENTATION_IDS.task,
  options: { operation: 'stop', kind: 'shell' }
}
const CHANNEL_MESSAGE: CoreFixturePresentation = { presentationId: CORE_TOOL_PRESENTATION_IDS.channel }
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

function reasoning(id: string): ConversationItem {
  return { id, type: 'reasoningContent', status: 'completed', reasoning: '**Checking**', createdAt: new Date().toISOString() }
}

function shape(
  items: ConversationItem[],
  context = { isRunning: false, isTrailingRun: false }
): Array<[string, string | string[]]> {
  return planToolRunRender(items, context).map((entry) => 'items' in entry
    ? [entry.kind, entry.items.map((item) => item.id)]
    : [entry.kind, entry.item.id])
}

describe('planToolRunRender', () => {
  it('keeps a lone finished call as its own row and groups missing-result calls of a completed turn', () => {
    expect(shape([makeItem(EXPLORE, 'ReadFile', '1')])).toEqual([['single', '1']])
    expect(shape([
      makeItem(EXPLORE, 'ReadFile', '1'),
      makeItem(EXPLORE, 'ReadFile', '2')
    ])).toEqual([['group', ['1', '2']]])
  })

  it('groups consecutive calls across categories, with reasoning inside the group', () => {
    expect(shape([
      makeItem(EXPLORE, 'ReadFile', '1'),
      reasoning('r1'),
      makeItem(SHELL, 'Exec', '2'),
      makeItem(EDIT, 'EditFile', '3'),
      makeItem(WEB_SEARCH, 'WebSearch', '4'),
      makeItem(RECALL, 'ReadFile', '5')
    ])).toEqual([['group', ['1', 'r1', '2', '3', '4', '5']]])
  })

  it('ends a group at a call that renders on its own', () => {
    expect(shape([
      makeItem(EXPLORE, 'ReadFile', '1'),
      makeItem(SHELL, 'Exec', '2'),
      makeItem(SUBAGENT_LIST, 'ListAgents', '3'),
      makeItem(WRITE, 'WriteFile', '4'),
      makeItem(WEB_FETCH, 'WebFetch', '5')
    ])).toEqual([
      ['group', ['1', '2']],
      ['single', '3'],
      ['group', ['4', '5']]
    ])
  })

  it('renders the trailing run of a running turn as one active row, leaving lone reasoning on its own', () => {
    expect(shape([reasoning('r1')], { isRunning: true, isTrailingRun: true })).toEqual([['reasoning', 'r1']])
    expect(shape([
      makeItem(SUBAGENT_LIST, 'ListAgents', '1'),
      makeItem(EXPLORE, 'ReadFile', '2'),
      makeItem(SHELL, 'Exec', '3', { status: 'started' }),
      reasoning('r1')
    ], { isRunning: true, isTrailingRun: true })).toEqual([
      ['single', '1'],
      ['active', ['2', '3', 'r1']]
    ])
  })

  it('splits a live call out of an earlier group while the turn runs', () => {
    expect(shape([
      makeItem(EXPLORE, 'ReadFile', '1', { result: 'ok', success: true }),
      makeItem(EXPLORE, 'ReadFile', '2'),
      makeItem(EXPLORE, 'ReadFile', '3', { result: 'ok', success: true }),
      makeItem(EXPLORE, 'ReadFile', '4', { result: 'ok', success: true })
    ], { isRunning: true, isTrailingRun: false })).toEqual([
      ['single', '1'],
      ['single', '2'],
      ['group', ['3', '4']]
    ])
  })

  it('treats completed tool calls without tool results as live only while the turn is running', () => {
    const pending = makeItem(SUBAGENT_MESSAGE, 'SendMessage', 'message-1', {
      arguments: { target: '/root/reviewer' }
    })

    expect(isToolItemLive(pending)).toBe(false)
    expect(isToolItemLive(pending, { turnRunning: true })).toBe(true)
  })

  it('groups settled spawns, keeping a starting spawn on the line', () => {
    expect(shape([
      makeItem(SUBAGENT_SPAWN, 'SpawnAgent', '1'),
      makeItem(SUBAGENT_SPAWN, 'SpawnAgent', '2', { status: 'started' }),
      makeItem(SUBAGENT_MESSAGE, 'SendMessage', '3')
    ])).toEqual([
      ['subagents', ['1', '2']],
      ['single', '3']
    ])
  })

  it('keeps a stopped background task on its own row between grouped calls', () => {
    expect(shape([
      makeItem(EXPLORE, 'ReadFile', '1'),
      makeItem(EXPLORE, 'ReadFile', '2'),
      makeItem(TASK_STOP, 'TaskStop', '3'),
      makeItem(TASK_STOP, 'TaskStop', '4'),
      makeItem(EXPLORE, 'ReadFile', '5')
    ])).toEqual([
      ['group', ['1', '2']],
      ['single', '3'],
      ['single', '4'],
      ['single', '5']
    ])
  })

  it('ends a group with a channel message, failed or not', () => {
    expect(shape([
      makeItem(EXPLORE, 'ReadFile', '1'),
      makeItem(EXPLORE, 'ReadFile', '2'),
      makeItem(CHANNEL_MESSAGE, 'SendChannelMessage', '3', { success: false }),
      makeItem(EXPLORE, 'ReadFile', '4'),
      makeItem(EXPLORE, 'ReadFile', '5')
    ])).toEqual([
      ['group', ['1', '2']],
      ['single', '3'],
      ['group', ['4', '5']]
    ])
  })

  it('folds memory updates and other tools into the group but views images on their own row', () => {
    const image = { contentItems: [{ type: 'image' as const, dataBase64: 'AA==', mediaType: 'image/png' }] }
    expect(shape([
      makeItem(WRITE, 'WriteFile', '1'),
      makeItem(MEMORY_EDIT, 'EditFile', '2', { success: false }),
      makeItem(EXPLORE, 'ReadFile', '3', image),
      makeItem(EXPLORE, 'ReadFile', '4', image),
      makeItem(SHELL, 'Exec', '5'),
      { id: '6', type: 'mcpToolCall', status: 'completed', toolName: 'lookup', createdAt: new Date().toISOString() }
    ])).toEqual([
      ['group', ['1', '2']],
      ['images', ['3', '4']],
      ['group', ['5', '6']]
    ])
  })
})

describe('resolveShellBackgroundState', () => {
  const live = makeItem(SHELL, 'Exec', 'live', { executionStatus: 'inProgress', terminalSessionId: 'term_1' })
  const stopped = makeItem(SHELL, 'Exec', 'stopped', { executionStatus: 'cancelled', terminalSessionId: 'term_1', success: true })

  it('marks commands that outlive their turn as running or stopped background terminals', () => {
    expect(resolveShellBackgroundState(live, { turnRunning: true })).toBeNull()
    expect(resolveShellBackgroundState(stopped, { turnRunning: true })).toBeNull()
    expect(resolveShellBackgroundState(live)).toBe('backgroundRunning')
    expect(resolveShellBackgroundState(makeItem(SHELL, 'Exec', 'term', { executionStatus: 'completed', terminalRunning: true })))
      .toBe('backgroundRunning')
    expect(resolveShellBackgroundState(stopped)).toBe('backgroundStopped')
  })

  it('leaves other finished commands and non-shell tools alone', () => {
    expect(resolveShellBackgroundState(makeItem(SHELL, 'Exec', 'a', { executionStatus: 'cancelled', success: true }))).toBeNull()
    expect(resolveShellBackgroundState({ ...stopped, success: undefined })).toBeNull()
    expect(resolveShellBackgroundState(makeItem(SHELL, 'Exec', 'b', { executionStatus: 'completed', terminalSessionId: 'term_1' }))).toBeNull()
    expect(resolveShellBackgroundState(makeItem(EXPLORE, 'ReadFile', 'c', { status: 'streaming' }))).toBeNull()
  })
})

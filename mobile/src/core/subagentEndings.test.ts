import { afterEach, describe, expect, it } from 'vitest'
import { createStudio } from '../demo/seed'
import { createHarness, waitFor, type Harness } from '../test/harness'
import { agentTask, terminalTask, workflowTask } from './backgroundTasks'
import { emptyHistory, type ChatHistory, type HistoryItem } from './history'
import { chatKey, initialState, reducer, type ComputerState } from './state'
import { createWorkingSubagentSelector } from './taskTree'
import { createTranscriptProjector, type ToolEntry, type TranscriptEntry } from './transcript'

const parent = 'parent'
const child = 'child'
const path = '/root/builder'
const key = chatKey('project', parent)
const harnesses: Harness[] = []

afterEach(() => {
  for (const harness of harnesses.splice(0)) harness.session.dispose()
})

function stateOf(): ComputerState {
  const state = reducer(initialState(), {
    type: 'paired',
    computer: { id: 'pc', name: 'PC', version: null, port: 1, fingerprint: '', addresses: [], relay: null, lastAddress: null, deviceId: 'phone', pairedAt: '' },
  }).computers.pc
  state.chats[key] = { key, projectId: 'project', threadId: parent, title: null, updatedAt: null, runtime: null, lastTurnFailed: false, profileId: null, readState: null }
  addChild(state, parent, child, path)
  return state
}

function addChild(state: ComputerState, owner: string, id: string, agentPath: string) {
  const task = agentTask({ edge: { childThreadId: id, parentThreadId: owner, agentPath, agentNickname: 'Builder', status: 'open' } })!
  const ownerKey = chatKey('project', owner)
  const sources = state.tasks[ownerKey] ?? {}
  state.tasks[ownerKey] = { ...sources, agent: [...(sources.agent ?? []), task] }
  return task
}

function addTerminal(state: ComputerState, threadId: string) {
  state.tasks[chatKey('project', threadId)] = {
    ...state.tasks[chatKey('project', threadId)],
    shell: [terminalTask({ sessionId: 'term_test', threadId, command: 'npm test', status: 'running' })!],
  }
}

function notice(id: string, status = 'completed', deliveryMode = 'guidance', agentPath = path): HistoryItem {
  return {
    id, turnId: 'turn', type: 'userMessage', status: 'completed',
    payload: { text: 'Ended', deliveryMode, triggerKind: 'subagent', triggerLabel: 'Builder', triggerRefId: agentPath, triggerStatus: status },
    createdAt: '2026-11-13T00:00:00Z', completedAt: null, partial: false,
  }
}

function historyOf(...items: HistoryItem[]): ChatHistory {
  return { ...emptyHistory(), items, turns: [{ id: 'turn', status: 'running', error: null }] }
}

function tools(entries: TranscriptEntry[]): ToolEntry[] {
  return entries.flatMap((entry) =>
    entry.kind === 'tool' ? [entry] : entry.kind === 'activity' || entry.kind === 'toolGroup' ? tools(entry.children) : [])
}

describe('subagent endings with background work', () => {
  it('includes paused workflows, then clears the ending when the workflow stops', () => {
    const state = stateOf()
    const sources = { workflow: [workflowTask({
      runId: 'run_test', threadId: child, status: 'paused', name: 'Checks', description: '', createdAt: '',
      controls: { canPause: false, canResume: true, canStop: true },
      totals: { agentCount: 0, queuedCount: 0, runningCount: 0, completedCount: 0, failedCount: 0, stoppedCount: 0, replayedCount: 0, inputTokens: 0, outputTokens: 0, toolCallCount: 0 },
    })] }
    state.tasks[chatKey('project', child)] = sources
    const select = createWorkingSubagentSelector()
    expect(select(state, key)).toEqual(new Set([path]))
    sources.workflow = sources.workflow.map((task) => ({ ...task, status: 'stopped' }))
    expect(select(state, key).size).toBe(0)
  })

  it('includes working descendants and their background work through idle intermediates', () => {
    const state = stateOf()
    const grandchild = addChild(state, child, 'grandchild', '/root/builder/checker')
    grandchild.status = 'running'
    const select = createWorkingSubagentSelector()
    expect(select(state, key)).toEqual(new Set([path, '/root/builder/checker']))

    grandchild.status = 'completed'
    addTerminal(state, 'grandchild')
    expect(select(state, key)).toEqual(new Set([path, '/root/builder/checker']))

    state.tasks[chatKey('project', 'grandchild')].shell = []
    expect(select(state, key).size).toBe(0)
  })

  it('treats a later active turn on the same child as running', () => {
    const state = stateOf()
    state.tasks[key].agent![0].status = 'running'
    expect(createWorkingSubagentSelector()(state, key)).toEqual(new Set([path]))
  })

  it('ignores unrelated roots and projects, closed branches, and display-name collisions', () => {
    const state = stateOf()
    addTerminal(state, parent)
    addTerminal(state, 'unrelated')
    const sibling = addChild(state, parent, 'sibling', '/root/other')
    addTerminal(state, sibling.id)
    state.tasks[chatKey('other-project', child)] = { shell: [terminalTask({ sessionId: 'term_foreign', threadId: child, status: 'running' })!] }
    const select = createWorkingSubagentSelector()
    expect(select(state, key)).toEqual(new Set(['/root/other']))
    const history = historyOf(notice('builder'))
    expect(tools(createTranscriptProjector().project(history, null, { workingAgentPaths: select(state, key) }))[0].verb).toBe('agentFinished')

    if (sibling.kind === 'agent') sibling.open = false
    expect(select(state, key).size).toBe(0)
  })

  it('retains failed and cancelled outcomes while the child still has work', () => {
    const history = historyOf(notice('failed', 'failed'), notice('cancelled', 'cancelled'))
    const entries = tools(createTranscriptProjector().project(history, null, { workingAgentPaths: new Set([path]) }))
    expect(entries.map((entry) => entry.verb)).toEqual(['agentInterrupted', 'agentInterrupted'])
  })

  it('keeps mixed endings independent and updates wake accessibility metadata too', () => {
    const projector = createTranscriptProjector()
    const history = historyOf(notice('wake', 'completed', 'queued'), notice('builder'), notice('other', 'completed', 'guidance', '/root/other'))
    const active = projector.project(history, null, { workingAgentPaths: new Set([path]) })
    expect(tools(active)).toMatchObject([{ id: 'turn/other', verb: 'agentFinished' }])
    expect(active.find((entry) => entry.kind === 'activity')).toMatchObject({ origin: { task: 'subagent', status: null } })
    expect(history.items[0].payload.triggerStatus).toBe('completed')

    const settled = projector.project(history, null, { workingAgentPaths: new Set() })
    expect(tools(settled).map((entry) => entry.verb)).toEqual(['agentFinished', 'agentFinished'])
    expect(settled.find((entry) => entry.kind === 'activity')).toMatchObject({ origin: { task: 'subagent', status: 'completed' } })
  })

  it('restores the original completion from background notifications without new transcript items', async () => {
    const computer = createStudio(new Date())
    computer.streamDelayMs = 1
    const parentId = 'thread_release_review'
    const { project, thread } = computer.thread(parentId)
    const at = new Date().toISOString()
    computer.background.agents.push({ childThreadId: child, parentThreadId: parentId, nickname: 'Builder', role: 'worker', running: false, createdAt: at, updatedAt: at })
    project.threads.push({ ...thread, id: child, source: 'subagent', items: [], turns: [], pending: [] })
    computer.background.terminals.push({ sessionId: 'term_child', threadId: child, command: 'npm test', status: 'running', startedAt: at, output: '' })
    const harness = createHarness([computer])
    harnesses.push(harness)
    await harness.session.boot()
    const state = () => harness.computerState()
    await waitFor(() => state().link === 'online' && !state().syncing)
    const parentKey = chatKey(project.id, parentId)
    harness.link().openChat(parentKey)
    const select = createWorkingSubagentSelector()
    await waitFor(() => select(state(), parentKey).has(path))
    const projector = createTranscriptProjector()
    const history = historyOf(notice('ended'))
    const endings = () => tools(projector.project(history, null, { workingAgentPaths: select(state(), parentKey) }))
    expect(endings()).toEqual([])
    expect(state().details[chatKey(project.id, child)]).toBeUndefined()
    computer.background.finishTerminal('term_child')
    await waitFor(() => endings()[0]?.verb === 'agentFinished')
  })
})

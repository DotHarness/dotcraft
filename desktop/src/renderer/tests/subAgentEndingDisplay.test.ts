import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import type { ConversationItem } from '../types/conversation'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useSubAgentTaskActivity } from '../stores/subAgentTaskActivity'
import { useThreadStore } from '../stores/threadStore'
import { subAgentEndingEntries, subAgentEndingOutcome } from '../utils/subAgentEndingDisplay'
import { makeSubAgent } from './subAgentFixtures'

const parent = 'parent-B'
const child = makeSubAgent()

function notice(id: string, path = child.agentPath, status = 'completed'): ConversationItem {
  return { id, type: 'userMessage', status: 'completed', deliveryMode: 'guidance', triggerKind: 'subagent',
    triggerRefId: path ?? undefined, triggerStatus: status }
}

function ownTerminal(threadId = child.childThreadId): void {
  useSubAgentTaskActivity.setState({ tasksByThread: new Map([[threadId, {
    terminals: [{ sessionId: 'term-test', command: 'work', startedAt: null }], workflows: []
  }]]) })
}

beforeEach(() => {
  useSubAgentStore.getState().reset()
  useThreadStore.getState().reset()
  useSubAgentStore.setState({ childrenByParent: new Map([[parent, [child]]]) })
})

afterEach(() => {
  useSubAgentStore.getState().reset()
  useThreadStore.getState().reset()
})

describe('subagent ending display', () => {
  it('filters only working children and restores their original notices when background work or a later turn settles', () => {
    const children = ['a', 'b'].map(id => makeSubAgent({ childThreadId: id, agentPath: `/root/${id}` }))
    useSubAgentStore.setState({ childrenByParent: new Map([[parent, children]]) })
    const lookup = { sourceThreadId: parent, childrenByParent: useSubAgentStore.getState().childrenByParent, threadList: [] }
    const items = children.map(child => notice(child.childThreadId, child.agentPath))
    const entries = () => subAgentEndingEntries(items, lookup, false, 'Agent').map(entry => ({ id: entry.id, child: entry.childThreadId }))
    ownTerminal('a')
    expect(entries()).toEqual([{ id: 'b', child: 'b' }])
    expect(subAgentEndingEntries([items[0]], lookup, false, 'Agent')).toEqual([])
    ownTerminal('unrelated')
    expect(entries()).toEqual([{ id: 'a', child: 'a' }, { id: 'b', child: 'b' }])
    useThreadStore.getState().applyRuntimeSnapshot('a', { running: true }, { isDesktopOrigin: false })
    expect(entries()).toEqual([{ id: 'b', child: 'b' }])
    useThreadStore.getState().applyRuntimeSnapshot('a', { running: false }, { isDesktopOrigin: false })
    expect(entries()).toEqual([{ id: 'a', child: 'a' }, { id: 'b', child: 'b' }])
  })

  it('suppresses successful completion for paused workflows and working descendants', () => {
    useSubAgentTaskActivity.setState({ tasksByThread: new Map([[child.childThreadId, {
      terminals: [], workflows: [{ runId: 'run-paused', name: 'Review', paused: true, startedAt: null }]
    }]]) })
    expect(subAgentEndingOutcome('completed', child)).toBeNull()
    useSubAgentTaskActivity.setState({ tasksByThread: new Map() })
    const descendant = makeSubAgent({ childThreadId: 'grandchild', parentThreadId: child.childThreadId })
    useSubAgentStore.setState({ childrenByParent: new Map([[parent, [child]], [child.childThreadId, [descendant]]]) })
    useThreadStore.getState().applyRuntimeSnapshot(descendant.childThreadId, { running: true }, { isDesktopOrigin: false })
    expect(subAgentEndingOutcome('completed', child)).toBeNull()
    useThreadStore.getState().applyRuntimeSnapshot(descendant.childThreadId, { running: false }, { isDesktopOrigin: false })
    expect(subAgentEndingOutcome('completed', child)).toBe('finished')
  })

  it('retains historical failure, cancellation, and stop records for a working child', () => {
    ownTerminal()
    const stop: ConversationItem = {
      id: 'stop', type: 'toolCall', status: 'completed', toolName: 'TaskStop', success: true,
      source: { kind: 'CoreNative', sourceId: 'core-native', sourceToolId: 'TaskStop' },
      presentation: { presentationId: 'core.task', options: { operation: 'stop', kind: 'agent' } },
      arguments: { taskId: 'other' }, result: JSON.stringify({ taskId: child.agentPath, kind: 'agent', status: 'stopped' })
    }
    const lookup = { sourceThreadId: parent, childrenByParent: useSubAgentStore.getState().childrenByParent, threadList: [] }
    const items = [notice('failed', child.agentPath, 'failed'), notice('cancelled', child.agentPath, 'cancelled'), stop]
    expect(subAgentEndingEntries(items, lookup, false, 'Agent').map(entry => ({ id: entry.id, child: entry.childThreadId }))).toEqual([
      { id: 'failed', child: child.childThreadId }, { id: 'cancelled', child: child.childThreadId }, { id: 'stop', child: child.childThreadId }
    ])
  })
})

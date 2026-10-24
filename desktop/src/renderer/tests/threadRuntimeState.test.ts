import { beforeEach, describe, expect, it } from 'vitest'
import { useThreadStore } from '../stores/threadStore'
import type { Thread, ThreadRuntimeSnapshot, ThreadSummary } from '../types/thread'
import { runtimeSnapshotFromThread } from '../utils/threadRuntime'

const store = () => useThreadStore.getState()
const idle: ThreadRuntimeSnapshot = {
  running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false
}
const running: ThreadRuntimeSnapshot = {
  ...idle, running: true, busy: true,
  activeTurnId: 'turn-active', activeTurnStartedAt: '2026-10-24T00:00:00Z'
}
const options = { isActive: true, isDesktopOrigin: true }

function thread(id: string, runtime = running): ThreadSummary {
  return {
    id, displayName: id, status: 'active', originChannel: 'dotcraft-desktop',
    createdAt: '2026-10-24T00:00:00Z', lastActiveAt: '2026-10-24T00:00:00Z', runtime
  }
}

beforeEach(() => store().reset())

describe('thread runtime synchronization', () => {
  it('atomically clears a completed turn across summaries, the active thread, and indicators', () => {
    store().setThreadList([thread('active'), thread('other')])
    store().setActiveThreadId('active')
    store().setActiveThread({ ...thread('active'), workspacePath: '/workspace', turns: [], metadata: {} } as Thread)
    const other = store().threadList[1]
    const observed: Array<Array<boolean | undefined>> = []
    const unsubscribe = useThreadStore.subscribe((state) => {
      observed.push([
        state.threadList[0].runtime?.running,
        state.activeThread?.runtime?.running,
        state.runtimeSnapshots.get('active')?.running,
        state.runningTurnThreadIds.has('active')
      ])
    })

    store().applyRuntimeSnapshot('active', idle, options)
    unsubscribe()

    expect(observed).toEqual([[false, false, false, false]])
    expect(store().threadList[0].runtime).toMatchObject({
      running: false, busy: false, activeTurnId: null, activeTurnStartedAt: null,
      waitingOnApproval: false, waitingOnInput: false, waitingOnPlanConfirmation: false
    })
    expect(store().runningTurnThreadIds.has('active')).toBe(false)
    expect(store().threadList[1]).toBe(other)
  })

  it.each(['setThreadList', 'upsertThreads'] as const)('%s preserves active turn identity and clears it on idle snapshots', (action) => {
    store().setThreadList([thread('active')])
    store().setActiveThread({ ...thread('active'), workspacePath: '/workspace', turns: [] } as Thread)
    store()[action]([thread('active')])
    expect(store().runtimeSnapshots.get('active')).toMatchObject(running)
    expect(store().activeThread?.runtime).toMatchObject(running)
    store()[action]([thread('active', idle)])
    expect(store().runtimeSnapshots.get('active')).toMatchObject({ activeTurnId: null, activeTurnStartedAt: null })
    expect(store().threadList[0].runtime).toEqual(store().runtimeSnapshots.get('active'))
    expect(store().activeThread?.runtime).toEqual(store().runtimeSnapshots.get('active'))
  })

  it('replaces resolved waiting state while retaining a distinct plan confirmation', () => {
    store().setThreadList([thread('background', { ...running, waitingOnApproval: true, waitingOnInput: true })])
    store().parkApproval('background', { bridgeId: 'approval', turnId: 'turn-active', rawParams: {} })
    store().parkUserInput('background', { bridgeId: 'input', turnId: 'turn-active', rawParams: {} })
    store().applyRuntimeSnapshot('background', { ...idle, waitingOnPlanConfirmation: true }, {
      isActive: false, isDesktopOrigin: true
    })
    expect(store().parkedApprovals.has('background')).toBe(false)
    expect(store().parkedUserInputs.has('background')).toBe(false)
    expect(store().pendingApprovalThreadIds.has('background')).toBe(false)
    expect(store().pendingUserInputThreadIds.has('background')).toBe(false)
    expect(store().pendingPlanConfirmationThreadIds.has('background')).toBe(true)
    expect(store().unreadCompletedThreadIds.has('background')).toBe(true)
    expect(store().threadList[0].runtime).toMatchObject({ activeTurnId: null, waitingOnPlanConfirmation: true })
  })

  it('updates child runtime without making completed children unread or inserting unknown rows', () => {
    store().setThreadList([{
      ...thread('child'), originChannel: 'subagent',
      source: { kind: 'subagent', subAgent: { parentThreadId: 'parent', depth: 1 } }
    }])
    store().applyRuntimeSnapshot('child', idle, { isActive: false, isDesktopOrigin: false })
    store().applyRuntimeSnapshot('unknown', running, options)
    expect(store().threadList.map(entry => entry.id)).toEqual(['child'])
    expect(store().threadList[0].runtime?.running).toBe(false)
    expect(store().unreadCompletedThreadIds.has('child')).toBe(false)
  })

  it('uses an explicit server runtime instead of stale history when reading a thread', () => {
    const snapshot = runtimeSnapshotFromThread({
      ...thread('active', idle), workspacePath: '/workspace',
      turns: [{ id: 'stale-turn', status: 'running', createdAt: '2026-10-24T00:00:00Z' }]
    } as Thread)
    expect(snapshot).toMatchObject({ running: false, activeTurnId: null, activeTurnStartedAt: null })
  })
})

import { describe, it, expect, beforeEach, vi } from 'vitest'
import { useThreadStore, selectFilteredThreads } from '../stores/threadStore'
import type { ThreadSummary, Thread, ThreadGoal } from '../types/thread'
import { registerVoiceOriginCleanup } from '../voice/voiceOriginCleanupBridge'

const settingsSet = vi.fn()

function makeThreadSummary(id: string, overrides: Partial<ThreadSummary> = {}): ThreadSummary {
  return {
    id,
    displayName: `Thread ${id}`,
    status: 'active',
    originChannel: 'test',
    createdAt: '2024-01-01T00:00:00Z',
    lastActiveAt: '2024-01-01T12:00:00Z',
    ...overrides
  }
}

function makeThread(id: string, overrides: Partial<Thread> = {}): Thread {
  return {
    ...makeThreadSummary(id),
    workspacePath: '/test/workspace',
    userId: 'local',
    metadata: {},
    turns: [],
    ...overrides
  }
}

function makeWorktreeThread(id: string, overrides: Partial<Thread> = {}): Thread {
  return makeThread(id, {
    workspacePath: '/test/workspace',
    effectiveWorkspacePath: '/test/workspace/.craft/worktrees/dotcraft-handoff',
    worktree: {
      id: 'worktree-1',
      sourceThreadId: id,
      workspacePath: '/test/workspace',
      sourceWorkspacePath: '/test/workspace',
      path: '/test/workspace/.craft/worktrees/dotcraft-handoff',
      branchName: 'dotcraft/handoff',
      baseRef: 'main',
      head: 'abc123',
      createdAt: '2026-01-01T00:00:00Z'
    },
    ...overrides
  })
}

function makeGoal(threadId: string, overrides: Partial<ThreadGoal> = {}): ThreadGoal {
  return {
    threadId,
    objective: `Goal ${threadId}`,
    status: 'active',
    tokenBudget: null,
    tokensUsed: 0,
    timeUsedSeconds: 0,
    createdAt: 1704067200,
    updatedAt: 1704067200,
    ...overrides
  }
}

function subagentSummary(id: string, parentThreadId: string, overrides: Partial<ThreadSummary> = {}): ThreadSummary {
  return makeThreadSummary(id, {
    originChannel: 'subagent',
    source: { kind: 'subagent', subAgent: { parentThreadId, depth: 1 } },
    ...overrides
  })
}

const s = () => useThreadStore.getState()

beforeEach(() => {
  vi.clearAllMocks()
  settingsSet.mockResolvedValue({})
  Object.defineProperty(globalThis, 'window', {
    configurable: true,
    value: {
      api: {
        settings: { set: settingsSet },
        workspace: {
          viewer: {
            browser: { destroy: vi.fn() },
            terminal: { dispose: vi.fn() }
          }
        }
      }
    }
  })
  s().reset()
})

describe('threadStore.setThreadList', () => {
  it('replaces the list, records the canonical project key, and clears it on reset', () => {
    s().setThreadList([makeThreadSummary('old')])
    s().setThreadList([makeThreadSummary('a'), makeThreadSummary('b')], 'X:\\fixtures\\workspace\\')
    expect(s().threadList.map((t) => t.id)).toEqual(['a', 'b'])
    expect(s().threadListProjectKey).toBe('x:/fixtures/workspace')

    s().reset()
    expect(s().threadListProjectKey).toBeNull()
  })

  it('filters internal helper threads from the thread list', () => {
    s().setThreadList([
      makeThreadSummary('visible'),
      makeThreadSummary('welcome', { originChannel: 'welcome-suggest' }),
      makeThreadSummary('metadata', { metadata: { 'dotcraft.internal': 'background-helper' } })
    ])

    expect(s().threadList.map((thread) => thread.id)).toEqual(['visible'])
  })

  it('hydrates runtime indicators, surfacing approval and input only for desktop-origin threads', () => {
    s().setThreadList([
      makeThreadSummary('running', {
        originChannel: 'dotcraft-desktop',
        runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }),
      makeThreadSummary('idle', {
        runtime: { running: false, waitingOnApproval: false, waitingOnPlanConfirmation: false }
      }),
      makeThreadSummary('approval', {
        originChannel: 'dotcraft-desktop',
        runtime: { running: true, waitingOnApproval: true, waitingOnPlanConfirmation: false }
      }),
      makeThreadSummary('input', {
        originChannel: 'dotcraft-desktop',
        runtime: { running: true, waitingOnApproval: false, waitingOnInput: true, waitingOnPlanConfirmation: false }
      }),
      makeThreadSummary('external', {
        originChannel: 'telegram',
        runtime: { running: true, waitingOnApproval: true, waitingOnInput: true, waitingOnPlanConfirmation: false }
      })
    ])

    const state = s()
    expect([...state.runningTurnThreadIds].sort()).toEqual(['approval', 'external', 'input', 'running'])
    expect(state.runtimeSnapshots.get('running')).toEqual({
      running: true,
      activeTurnId: null,
      activeTurnStartedAt: null,
      busy: false,
      waitingOnApproval: false,
      waitingOnInput: false,
      waitingOnPlanConfirmation: false,
      maintenanceKind: null
    })
    expect([...state.pendingApprovalThreadIds]).toEqual(['approval'])
    expect([...state.pendingUserInputThreadIds]).toEqual(['input'])
  })

  it('hydrates goal snapshots and drops goals for threads no longer listed', () => {
    const goal = makeGoal('goal-thread')
    s().setThreadGoal(makeGoal('old-thread'))
    s().setThreadList([makeThreadSummary('goal-thread', { goal }), makeThreadSummary('plain-thread')])

    expect(s().goalSnapshots.get('goal-thread')).toEqual(goal)
    expect(s().goalSnapshots.has('plain-thread')).toBe(false)
    expect(s().goalSnapshots.has('old-thread')).toBe(false)
  })
})

describe('threadStore.addThread', () => {
  it('prepends new threads, skips duplicate ids and internal helper threads', () => {
    s().setThreadList([makeThreadSummary('existing')])
    s().addThread(makeThreadSummary('new'))
    s().addThread(makeThreadSummary('new'))
    s().addThread(makeThreadSummary('existing'))
    s().addThread(makeThreadSummary('welcome', { originChannel: 'welcome-suggest' }))
    s().addThread(makeThreadSummary('commit', { metadata: { 'dotcraft.internal': 'commit-suggest' } }))

    expect(s().threadList.map((t) => t.id)).toEqual(['new', 'existing'])
  })
})

describe('threadStore.upsertThreads', () => {
  it('filters internal helper threads from upserts', () => {
    s().setThreadList([makeThreadSummary('visible', { displayName: 'Old' })])

    s().upsertThreads([
      makeThreadSummary('visible', { displayName: 'New' }),
      makeThreadSummary('welcome', { originChannel: 'welcome-suggest' }),
      makeThreadSummary('metadata', { metadata: { 'dotcraft.internal': 'background-helper' } })
    ])

    expect(s().threadList.map((thread) => thread.id)).toEqual(['visible'])
    expect(s().threadList[0].displayName).toBe('New')
  })

  it('removes a previously visible thread when an upsert marks it internal', () => {
    const leaked = makeThreadSummary('leaked', {
      displayName: 'New conversation',
      originChannel: 'dotcraft-desktop',
      goal: makeGoal('leaked')
    })

    s().setThreadList([leaked, makeThreadSummary('visible')])
    s().applyRuntimeSnapshot('leaked', {
      running: true,
      waitingOnApproval: true,
      waitingOnInput: true,
      waitingOnPlanConfirmation: true
    }, {
      isActive: false,
      isDesktopOrigin: true
    })
    s().parkApproval('leaked', {
      bridgeId: 'approval-1',
      turnId: 'turn-1',
      rawParams: { threadId: 'leaked', requestId: 'request-1' }
    })
    s().parkUserInput('leaked', {
      bridgeId: 'input-1',
      turnId: 'turn-1',
      rawParams: { threadId: 'leaked', requestId: 'input-1', questions: [] }
    })
    s().markUnreadCompleted('leaked')

    s().upsertThreads([
      makeThreadSummary('leaked', {
        metadata: { 'dotcraft.internal': 'agent-builder' }
      })
    ])

    const state = s()
    expect(state.threadList.map((thread) => thread.id)).toEqual(['visible'])
    expect(state.runtimeSnapshots.has('leaked')).toBe(false)
    expect(state.runningTurnThreadIds.has('leaked')).toBe(false)
    expect(state.pendingApprovalThreadIds.has('leaked')).toBe(false)
    expect(state.pendingUserInputThreadIds.has('leaked')).toBe(false)
    expect(state.pendingPlanConfirmationThreadIds.has('leaked')).toBe(false)
    expect(state.unreadCompletedThreadIds.has('leaked')).toBe(false)
    expect(state.parkedApprovals.has('leaked')).toBe(false)
    expect(state.parkedUserInputs.has('leaked')).toBe(false)
    expect(state.goalSnapshots.has('leaked')).toBe(false)
  })

  it('keeps hidden threads out of running, unread, and badge state', () => {
    const runningSnapshot = {
      running: true,
      waitingOnApproval: true,
      waitingOnInput: false,
      waitingOnPlanConfirmation: false
    }
    s().setThreadList([makeThreadSummary('visible')])
    s().addThread(makeThreadSummary('fork', { ephemeral: true }))
    s().setActiveThread(makeThread('builder', { metadata: { 'dotcraft.internal': 'agent-builder' } }))

    for (const id of ['fork', 'builder']) {
      s().applyRuntimeSnapshot(id, runningSnapshot, { isActive: false, isDesktopOrigin: true })
      s().applyRuntimeSnapshot(id, { ...runningSnapshot, running: false, waitingOnApproval: false }, {
        isActive: false,
        isDesktopOrigin: true
      })
      s().markUnreadCompleted(id)
    }
    s().applyRuntimeSnapshot('visible', runningSnapshot, { isActive: false, isDesktopOrigin: true })

    const state = s()
    expect(state.threadList.map((thread) => thread.id)).toEqual(['visible'])
    expect([...state.runningTurnThreadIds]).toEqual(['visible'])
    expect([...state.pendingApprovalThreadIds]).toEqual(['visible'])
    expect(state.unreadCompletedThreadIds.size).toBe(0)
    expect(state.runtimeSnapshots.has('fork')).toBe(false)
    expect(state.runtimeSnapshots.has('builder')).toBe(false)
  })

  it('clears stale worktree metadata when a local snapshot omits worktree', () => {
    s().setThreadList([makeWorktreeThread('t1')])
    s().upsertThreads([
      makeThreadSummary('t1', { workspacePath: '/test/workspace', effectiveWorkspacePath: '/test/workspace' })
    ])

    expect(s().threadList[0].worktree).toBeNull()
    expect(s().threadList[0].effectiveWorkspacePath).toBe('/test/workspace')
  })
})

describe('threadStore thread mutations', () => {
  it('updates status and display name on the list entry and the active thread only', () => {
    s().setThreadList([makeThreadSummary('t1'), makeThreadSummary('t2')])
    s().setActiveThread(makeThread('t1'))

    s().updateThreadStatus('t1', 'archived')
    s().renameThread('t1', 'Renamed')

    expect(s().threadList[0]).toMatchObject({ status: 'archived', displayName: 'Renamed' })
    expect(s().threadList[1]).toMatchObject({ status: 'active', displayName: 'Thread t2' })
    expect(s().activeThread).toMatchObject({ status: 'archived', displayName: 'Renamed' })
  })

  it('removing the active thread clears list entry, active selection, and cached goal', () => {
    s().setThreadList([makeThreadSummary('t1'), makeThreadSummary('t2')])
    s().setActiveThreadId('t1')
    s().setActiveThread(makeThread('t1'))
    s().setThreadGoal(makeGoal('t1'))

    s().removeThread('t2')
    expect(s().activeThreadId).toBe('t1')

    s().removeThread('t1')
    expect(s().threadList).toEqual([])
    expect(s().activeThreadId).toBeNull()
    expect(s().activeThread).toBeNull()
    expect(s().goalSnapshots.has('t1')).toBe(false)
  })

  it('notifies voice cleanup for every removed thread-tree origin', () => {
    const cleanup = vi.fn()
    const unregister = registerVoiceOriginCleanup(cleanup)
    s().setThreadList([makeThreadSummary('parent'), subagentSummary('child', 'parent')])

    s().removeThreadTree('parent')

    expect(cleanup).toHaveBeenCalledWith(expect.arrayContaining(['parent', 'child']))
    unregister()
  })
})

describe('threadStore pinned threads', () => {
  it('hydrates pinned ids with trimming and de-duplication', () => {
    s().hydratePinnedThreadIds('/workspace', [' thread-a ', 'thread-b', 'thread-a', ''])

    expect(s().pinnedThreadWorkspacePath).toBe('/workspace')
    expect(s().pinnedThreadIds).toEqual(['thread-a', 'thread-b'])
  })

  it('toggles pins at the front and persists under the canonical workspace key', () => {
    s().setThreadList([makeThreadSummary('thread-a'), makeThreadSummary('thread-b')])
    s().hydratePinnedThreadIds('X:\\fixtures\\workspace\\', ['thread-a'])

    s().togglePinnedThread('thread-b')
    expect(s().pinnedThreadIds).toEqual(['thread-b', 'thread-a'])
    expect(settingsSet).toHaveBeenLastCalledWith({
      pinnedThreadIdsByWorkspace: { 'x:/fixtures/workspace': ['thread-b', 'thread-a'] }
    })

    s().togglePinnedThread('thread-b')
    expect(s().pinnedThreadIds).toEqual(['thread-a'])
    expect(settingsSet).toHaveBeenLastCalledWith({
      pinnedThreadIdsByWorkspace: { 'x:/fixtures/workspace': ['thread-a'] }
    })
  })

  it('does not pin subagent children', () => {
    s().setThreadList([makeThreadSummary('parent-1'), subagentSummary('child-1', 'parent-1')])
    s().hydratePinnedThreadIds('/workspace', [])

    s().togglePinnedThread('child-1')

    expect(s().pinnedThreadIds).toEqual([])
    expect(settingsSet).not.toHaveBeenCalled()
  })

  it('removes deleted threads from pinned ids', () => {
    s().setThreadList([makeThreadSummary('thread-a'), makeThreadSummary('thread-b')])
    s().hydratePinnedThreadIds('/workspace', ['thread-a', 'thread-b'])

    s().removeThread('thread-a')

    expect(s().pinnedThreadIds).toEqual(['thread-b'])
    expect(settingsSet).toHaveBeenCalledWith({ pinnedThreadIdsByWorkspace: { '/workspace': ['thread-b'] } })
  })

  it('prunes archived and missing pinned threads', () => {
    s().setThreadList([
      makeThreadSummary('active-thread'),
      makeThreadSummary('archived-thread', { status: 'archived' })
    ])
    s().hydratePinnedThreadIds('/workspace', ['active-thread', 'archived-thread', 'missing-thread'])

    s().prunePinnedThreadIds()

    expect(s().pinnedThreadIds).toEqual(['active-thread'])
    expect(settingsSet).toHaveBeenCalledWith({ pinnedThreadIdsByWorkspace: { '/workspace': ['active-thread'] } })
  })
})

describe('threadStore goal snapshots', () => {
  it('sets and clears goals on the snapshot map, list entry, and active thread', () => {
    const goal = makeGoal('t1')
    s().setThreadList([makeThreadSummary('t1')])
    s().setActiveThread(makeThread('t1'))

    s().setThreadGoal(goal)
    expect(s().goalSnapshots.get('t1')).toEqual(goal)
    expect(s().threadList[0].goal).toEqual(goal)
    expect(s().activeThread?.goal).toEqual(goal)

    s().clearThreadGoal('t1')
    expect(s().goalSnapshots.has('t1')).toBe(false)
    expect(s().threadList[0].goal).toBeNull()
    expect(s().activeThread?.goal).toBeNull()
  })
})

describe('threadStore.setActiveThread', () => {
  it('does not change activeThreadId when loading a different thread id (stale read guard)', () => {
    s().setActiveThreadId('A')
    s().setActiveThread(makeThread('B', { displayName: 'Loaded B' }))
    expect(s().activeThreadId).toBe('A')
    expect(s().activeThread?.id).toBe('B')
  })

  it('preserves turns while clearing stale worktree metadata from local snapshots', () => {
    s().setActiveThread(makeWorktreeThread('t1', {
      turns: [{ id: 'turn-1', status: 'completed', createdAt: '2026-01-01T00:00:00Z' }]
    }))
    s().setActiveThread(makeThread('t1', {
      workspacePath: '/test/workspace',
      effectiveWorkspacePath: '/test/workspace',
      turns: []
    }))

    expect(s().activeThread?.worktree).toBeNull()
    expect(s().activeThread?.effectiveWorkspacePath).toBe('/test/workspace')
    expect(s().activeThread?.turns).toHaveLength(1)
  })
})

describe('selectFilteredThreads', () => {
  beforeEach(() => {
    s().setThreadList([
      makeThreadSummary('t1', { displayName: 'Hello World' }),
      makeThreadSummary('t2', { displayName: 'Goodbye Planet' }),
      makeThreadSummary('t3', { displayName: null }),
      subagentSummary('sub-1', 't1', { displayName: 'Hello Subagent' })
    ])
  })

  it.each([
    ['', ['t1', 't2', 't3']],
    ['   ', ['t1', 't2', 't3']],
    ['hello', ['t1']],
    ['null', []],
    ['zzz-no-match', []]
  ])('query %j matches top-level threads %j', (query, expected) => {
    s().setSearchQuery(query)
    expect(selectFilteredThreads(s()).map((t) => t.id)).toEqual(expected)
  })
})

describe('threadStore indicator state', () => {
  it('parks approvals per thread, replacing replays of the same request, and consumes them', () => {
    s().parkApproval('t1', {
      bridgeId: 'bridge-1',
      turnId: 'turn-1',
      rawParams: { threadId: 't1', turnId: 'turn-1', requestId: 'req-1' }
    })
    s().parkApproval('t1', {
      bridgeId: 'bridge-2',
      turnId: 'turn-1',
      rawParams: { threadId: 't1', turnId: 'turn-1', requestId: 'req-2' }
    })
    s().parkApproval('t1', {
      bridgeId: 'bridge-2-replay',
      turnId: 'turn-1',
      rawParams: { threadId: 't1', turnId: 'turn-1', requestId: 'req-2' }
    })

    expect(s().parkedApprovals.get('t1')?.map((approval) => approval.bridgeId)).toEqual([
      'bridge-1',
      'bridge-2-replay'
    ])

    expect(s().consumeParkedApprovals('t1').map((approval) => approval.bridgeId)).toEqual(['bridge-1', 'bridge-2-replay'])
    expect(s().parkedApprovals.has('t1')).toBe(false)
  })

  it('parks and consumes user input requests by thread id', () => {
    s().parkUserInput('t1', {
      bridgeId: 'bridge-input',
      turnId: 'turn-1',
      rawParams: { threadId: 't1', turnId: 'turn-1', requestId: 'req-1', questions: [] }
    })
    expect(s().parkedUserInputs.has('t1')).toBe(true)

    const parked = s().consumeParkedUserInput('t1')
    expect(parked?.bridgeId).toBe('bridge-input')
    expect(parked?.rawParams.requestId).toBe('req-1')
    expect(s().parkedUserInputs.has('t1')).toBe(false)
  })

  it('tracks and clears pending plan/unread-completed on thread activation', () => {
    s().applyRuntimeSnapshot('thread-a', {
      running: false,
      waitingOnApproval: true,
      waitingOnPlanConfirmation: false
    }, {
      isActive: false,
      isDesktopOrigin: true
    })
    s().markPlanConfirmationPending('thread-a')
    s().markUnreadCompleted('thread-a')
    expect(s().pendingPlanConfirmationThreadIds.has('thread-a')).toBe(true)
    expect(s().pendingApprovalThreadIds.has('thread-a')).toBe(true)
    expect(s().unreadCompletedThreadIds.has('thread-a')).toBe(true)

    s().setActiveThreadId('thread-a')
    expect(s().pendingApprovalThreadIds.has('thread-a')).toBe(false)
    expect(s().pendingPlanConfirmationThreadIds.has('thread-a')).toBe(false)
    expect(s().unreadCompletedThreadIds.has('thread-a')).toBe(false)
  })

  it('applies runtime snapshots for running, approval, plan, and unread state', () => {
    s().setThreadList([makeThreadSummary('thread-a', { originChannel: 'dotcraft-desktop' })])

    s().applyRuntimeSnapshot('thread-a', {
      running: true,
      waitingOnApproval: true,
      waitingOnPlanConfirmation: false
    }, {
      isActive: false,
      isDesktopOrigin: true
    })

    expect(s().runningTurnThreadIds.has('thread-a')).toBe(true)
    expect(s().pendingApprovalThreadIds.has('thread-a')).toBe(true)
    expect(s().unreadCompletedThreadIds.has('thread-a')).toBe(false)

    s().applyRuntimeSnapshot('thread-a', {
      running: false,
      waitingOnApproval: false,
      waitingOnPlanConfirmation: true
    }, {
      isActive: false,
      isDesktopOrigin: true
    })

    expect(s().runningTurnThreadIds.has('thread-a')).toBe(false)
    expect(s().pendingApprovalThreadIds.has('thread-a')).toBe(false)
    expect(s().pendingPlanConfirmationThreadIds.has('thread-a')).toBe(true)
    expect(s().unreadCompletedThreadIds.has('thread-a')).toBe(true)
  })
})

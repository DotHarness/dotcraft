import { describe, it, expect, beforeEach, vi } from 'vitest'
import { useThreadStore, selectFilteredThreads } from '../stores/threadStore'
import type { ThreadSummary, Thread, ThreadGoal } from '../types/thread'

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
    goalId: `goal-${threadId}`,
    objective: `Goal ${threadId}`,
    status: 'active',
    tokenBudget: null,
    tokensUsed: {
      inputTokens: 0,
      outputTokens: 0,
      totalTokens: 0
    },
    timeUsedSeconds: 0,
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-01T00:00:00Z',
    ...overrides
  }
}

// Reset store between tests
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
  useThreadStore.getState().reset()
})

describe('threadStore.setThreadList', () => {
  it('sets the thread list', () => {
    const threads = [makeThreadSummary('a'), makeThreadSummary('b')]
    useThreadStore.getState().setThreadList(threads)
    expect(useThreadStore.getState().threadList).toEqual(threads)
  })

  it('records the canonical project key for the thread list', () => {
    const threads = [makeThreadSummary('a')]
    useThreadStore.getState().setThreadList(threads, 'F:\\Git\\dotcraft\\')
    expect(useThreadStore.getState().threadListProjectKey).toBe('f:/git/dotcraft')
  })

  it('clears the thread list project key on reset', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('a')], '/workspace/a')
    useThreadStore.getState().reset()
    expect(useThreadStore.getState().threadListProjectKey).toBeNull()
  })

  it('filters internal helper threads from the thread list', () => {
    useThreadStore.getState().setThreadList([
      makeThreadSummary('visible'),
      makeThreadSummary('welcome', { originChannel: 'welcome-suggest' }),
      makeThreadSummary('metadata', { metadata: { 'dotcraft.internal': 'background-helper' } })
    ])

    expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['visible'])
  })

  it('replaces existing list', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('old')])
    useThreadStore.getState().setThreadList([makeThreadSummary('new1'), makeThreadSummary('new2')])
    expect(useThreadStore.getState().threadList.map((t) => t.id)).toEqual(['new1', 'new2'])
  })

  it('hydrates running indicators from thread list runtime snapshots', () => {
    useThreadStore.getState().setThreadList([
      makeThreadSummary('running', {
        originChannel: 'dotcraft-desktop',
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }),
      makeThreadSummary('idle', {
        runtime: {
          running: false,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      })
    ])

    expect(useThreadStore.getState().runningTurnThreadIds.has('running')).toBe(true)
    expect(useThreadStore.getState().runningTurnThreadIds.has('idle')).toBe(false)
    expect(useThreadStore.getState().runtimeSnapshots.get('running')).toEqual({
      running: true,
      busy: false,
      waitingOnApproval: false,
      waitingOnInput: false,
      waitingOnPlanConfirmation: false,
      maintenanceKind: null
    })
  })

  it('hydrates pending approval indicators from desktop-origin runtime snapshots', () => {
    useThreadStore.getState().setThreadList([
      makeThreadSummary('approval', {
        originChannel: 'dotcraft-desktop',
        runtime: {
          running: true,
          waitingOnApproval: true,
          waitingOnPlanConfirmation: false
        }
      }),
      makeThreadSummary('external-approval', {
        originChannel: 'telegram',
        runtime: {
          running: true,
          waitingOnApproval: true,
          waitingOnPlanConfirmation: false
        }
      })
    ])

    expect(useThreadStore.getState().runningTurnThreadIds.has('approval')).toBe(true)
    expect(useThreadStore.getState().pendingApprovalThreadIds.has('approval')).toBe(true)
    expect(useThreadStore.getState().pendingApprovalThreadIds.has('external-approval')).toBe(false)
  })

  it('hydrates pending user input indicators from desktop-origin runtime snapshots', () => {
    useThreadStore.getState().setThreadList([
      makeThreadSummary('input', {
        originChannel: 'dotcraft-desktop',
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnInput: true,
          waitingOnPlanConfirmation: false
        }
      }),
      makeThreadSummary('external-input', {
        originChannel: 'telegram',
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnInput: true,
          waitingOnPlanConfirmation: false
        }
      })
    ])

    expect(useThreadStore.getState().runningTurnThreadIds.has('input')).toBe(true)
    expect(useThreadStore.getState().pendingUserInputThreadIds.has('input')).toBe(true)
    expect(useThreadStore.getState().pendingUserInputThreadIds.has('external-input')).toBe(false)
  })
})

describe('threadStore.addThread', () => {
  it('prepends thread to the list', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('existing')])
    useThreadStore.getState().addThread(makeThreadSummary('new'))
    const list = useThreadStore.getState().threadList
    expect(list[0].id).toBe('new')
    expect(list[1].id).toBe('existing')
  })

  it('adds to empty list', () => {
    useThreadStore.getState().addThread(makeThreadSummary('t1'))
    expect(useThreadStore.getState().threadList).toHaveLength(1)
  })

  it('does not add internal helper threads', () => {
    useThreadStore.getState().addThread(makeThreadSummary('welcome', { originChannel: 'welcome-suggest' }))
    useThreadStore.getState().addThread(
      makeThreadSummary('commit', { metadata: { 'dotcraft.internal': 'commit-suggest' } })
    )

    expect(useThreadStore.getState().threadList).toEqual([])
  })

  it('skips duplicate thread id (idempotent)', () => {
    const t = makeThreadSummary('same-id')
    useThreadStore.getState().addThread(t)
    useThreadStore.getState().addThread(t)
    expect(useThreadStore.getState().threadList).toHaveLength(1)
    expect(useThreadStore.getState().threadList[0].id).toBe('same-id')
  })

  it('allows adding threads with different ids', () => {
    useThreadStore.getState().addThread(makeThreadSummary('a'))
    useThreadStore.getState().addThread(makeThreadSummary('b'))
    expect(useThreadStore.getState().threadList.map((x) => x.id)).toEqual(['b', 'a'])
  })

  it('does not add duplicate when list already contains that id', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('existing')])
    useThreadStore.getState().addThread(makeThreadSummary('existing'))
    expect(useThreadStore.getState().threadList).toHaveLength(1)
    expect(useThreadStore.getState().threadList[0].id).toBe('existing')
  })

  it('hydrates goal snapshots from thread list entries', () => {
    const goal = makeGoal('goal-thread')
    useThreadStore.getState().setThreadList([
      makeThreadSummary('goal-thread', { goal }),
      makeThreadSummary('plain-thread')
    ])

    expect(useThreadStore.getState().goalSnapshots.get('goal-thread')).toEqual(goal)
    expect(useThreadStore.getState().goalSnapshots.has('plain-thread')).toBe(false)
  })

  it('removes stale goal snapshots when thread list omits the thread', () => {
    useThreadStore.getState().setThreadGoal(makeGoal('old-thread'))
    useThreadStore.getState().setThreadList([makeThreadSummary('new-thread')])

    expect(useThreadStore.getState().goalSnapshots.has('old-thread')).toBe(false)
  })
})

describe('threadStore.upsertThreads', () => {
  it('filters internal helper threads from upserts', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('visible', { displayName: 'Old' })])

    useThreadStore.getState().upsertThreads([
      makeThreadSummary('visible', { displayName: 'New' }),
      makeThreadSummary('welcome', { originChannel: 'welcome-suggest' }),
      makeThreadSummary('metadata', { metadata: { 'dotcraft.internal': 'background-helper' } })
    ])

    expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['visible'])
    expect(useThreadStore.getState().threadList[0].displayName).toBe('New')
  })

  it('removes a previously visible thread when an upsert marks it internal', () => {
    const leakedGoal = makeGoal('leaked')
    const leaked = makeThreadSummary('leaked', {
      displayName: 'New conversation',
      originChannel: 'dotcraft-desktop',
      goal: leakedGoal
    })

    useThreadStore.getState().setThreadList([leaked, makeThreadSummary('visible')])
    useThreadStore.getState().applyRuntimeSnapshot('leaked', {
      running: true,
      waitingOnApproval: true,
      waitingOnInput: true,
      waitingOnPlanConfirmation: true
    }, {
      isActive: false,
      isDesktopOrigin: true
    })
    useThreadStore.getState().parkApproval('leaked', {
      bridgeId: 'approval-1',
      turnId: 'turn-1',
      rawParams: { threadId: 'leaked', requestId: 'request-1' }
    })
    useThreadStore.getState().parkUserInput('leaked', {
      bridgeId: 'input-1',
      turnId: 'turn-1',
      rawParams: { threadId: 'leaked', requestId: 'input-1', questions: [] }
    })
    useThreadStore.getState().markUnreadCompleted('leaked')

    useThreadStore.getState().upsertThreads([
      makeThreadSummary('leaked', {
        metadata: { 'dotcraft.internal': 'agent-builder' }
      })
    ])

    const state = useThreadStore.getState()
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

  it('clears stale worktree metadata when a local snapshot omits worktree', () => {
    const worktreeThread = makeWorktreeThread('t1')
    const localThread = makeThreadSummary('t1', {
      workspacePath: '/test/workspace',
      effectiveWorkspacePath: '/test/workspace'
    })

    useThreadStore.getState().setThreadList([worktreeThread])
    useThreadStore.getState().upsertThreads([localThread])

    expect(useThreadStore.getState().threadList[0].worktree).toBeNull()
    expect(useThreadStore.getState().threadList[0].effectiveWorkspacePath).toBe('/test/workspace')
  })
})

describe('threadStore.updateThreadStatus', () => {
  it('updates the status of the matching thread', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('t1'), makeThreadSummary('t2')])
    useThreadStore.getState().updateThreadStatus('t1', 'paused')
    expect(useThreadStore.getState().threadList[0].status).toBe('paused')
    expect(useThreadStore.getState().threadList[1].status).toBe('active')
  })

  it('updates the active thread if it matches', () => {
    const thread = makeThread('t1')
    useThreadStore.getState().setActiveThread(thread)
    useThreadStore.getState().updateThreadStatus('t1', 'archived')
    expect(useThreadStore.getState().activeThread?.status).toBe('archived')
  })

  it('does not change other threads', () => {
    useThreadStore.getState().setThreadList([
      makeThreadSummary('t1'),
      makeThreadSummary('t2'),
      makeThreadSummary('t3')
    ])
    useThreadStore.getState().updateThreadStatus('t2', 'paused')
    expect(useThreadStore.getState().threadList[0].status).toBe('active')
    expect(useThreadStore.getState().threadList[2].status).toBe('active')
  })
})

describe('threadStore.removeThread', () => {
  it('removes the thread from the list', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('t1'), makeThreadSummary('t2')])
    useThreadStore.getState().removeThread('t1')
    expect(useThreadStore.getState().threadList.map((t) => t.id)).toEqual(['t2'])
  })

  it('clears any cached goal for the removed thread', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('t1')])
    useThreadStore.getState().setThreadGoal(makeGoal('t1'))

    useThreadStore.getState().removeThread('t1')

    expect(useThreadStore.getState().goalSnapshots.has('t1')).toBe(false)
  })

  it('clears activeThreadId when the active thread is removed', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('t1')])
    useThreadStore.getState().setActiveThreadId('t1')
    useThreadStore.getState().removeThread('t1')
    expect(useThreadStore.getState().activeThreadId).toBeNull()
  })

  it('clears activeThread when the active thread is removed', () => {
    const thread = makeThread('t1')
    useThreadStore.getState().setActiveThread(thread)
    useThreadStore.getState().removeThread('t1')
    expect(useThreadStore.getState().activeThread).toBeNull()
  })

  it('does not clear activeThreadId when a different thread is removed', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('t1'), makeThreadSummary('t2')])
    useThreadStore.getState().setActiveThreadId('t1')
    useThreadStore.getState().removeThread('t2')
    expect(useThreadStore.getState().activeThreadId).toBe('t1')
  })
})

describe('threadStore pinned threads', () => {
  it('hydrates pinned ids with trimming and de-duplication', () => {
    useThreadStore.getState().hydratePinnedThreadIds('/workspace', [
      ' thread-a ',
      'thread-b',
      'thread-a',
      ''
    ])

    expect(useThreadStore.getState().pinnedThreadWorkspacePath).toBe('/workspace')
    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['thread-a', 'thread-b'])
  })

  it('pins new top-level threads at the front and persists the workspace-scoped list', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('thread-a'), makeThreadSummary('thread-b')])
    useThreadStore.getState().hydratePinnedThreadIds('/workspace', ['thread-a'])

    useThreadStore.getState().togglePinnedThread('thread-b')

    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['thread-b', 'thread-a'])
    expect(settingsSet).toHaveBeenCalledWith({
      pinnedThreadIdsByWorkspace: {
        '/workspace': ['thread-b', 'thread-a']
      }
    })
  })

  it('persists pinned ids under the canonical workspace project key', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('thread-a')])
    useThreadStore.getState().hydratePinnedThreadIds('F:\\Git\\dotcraft\\', [])

    useThreadStore.getState().togglePinnedThread('thread-a')

    expect(settingsSet).toHaveBeenCalledWith({
      pinnedThreadIdsByWorkspace: {
        'f:/git/dotcraft': ['thread-a']
      }
    })
  })

  it('unpins an existing pinned thread and persists the removal', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('thread-a'), makeThreadSummary('thread-b')])
    useThreadStore.getState().hydratePinnedThreadIds('/workspace', ['thread-b', 'thread-a'])

    useThreadStore.getState().togglePinnedThread('thread-b')

    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['thread-a'])
    expect(settingsSet).toHaveBeenCalledWith({
      pinnedThreadIdsByWorkspace: {
        '/workspace': ['thread-a']
      }
    })
  })

  it('does not pin subagent children', () => {
    useThreadStore.getState().setThreadList([
      makeThreadSummary('parent-1'),
      makeThreadSummary('child-1', {
        originChannel: 'subagent',
        source: {
          kind: 'subagent',
          subAgent: {
            parentThreadId: 'parent-1',
            depth: 1
          }
        }
      })
    ])
    useThreadStore.getState().hydratePinnedThreadIds('/workspace', [])

    useThreadStore.getState().togglePinnedThread('child-1')

    expect(useThreadStore.getState().pinnedThreadIds).toEqual([])
    expect(settingsSet).not.toHaveBeenCalled()
  })

  it('removes deleted threads from pinned ids', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('thread-a'), makeThreadSummary('thread-b')])
    useThreadStore.getState().hydratePinnedThreadIds('/workspace', ['thread-a', 'thread-b'])

    useThreadStore.getState().removeThread('thread-a')

    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['thread-b'])
    expect(settingsSet).toHaveBeenCalledWith({
      pinnedThreadIdsByWorkspace: {
        '/workspace': ['thread-b']
      }
    })
  })

  it('prunes archived and missing pinned threads', () => {
    useThreadStore.getState().setThreadList([
      makeThreadSummary('active-thread'),
      makeThreadSummary('archived-thread', { status: 'archived' })
    ])
    useThreadStore.getState().hydratePinnedThreadIds('/workspace', [
      'active-thread',
      'archived-thread',
      'missing-thread'
    ])

    useThreadStore.getState().prunePinnedThreadIds()

    expect(useThreadStore.getState().pinnedThreadIds).toEqual(['active-thread'])
    expect(settingsSet).toHaveBeenCalledWith({
      pinnedThreadIdsByWorkspace: {
        '/workspace': ['active-thread']
      }
    })
  })
})

describe('threadStore.renameThread', () => {
  it('updates the displayName of the matching thread', () => {
    useThreadStore.getState().setThreadList([makeThreadSummary('t1')])
    useThreadStore.getState().renameThread('t1', 'My renamed thread')
    expect(useThreadStore.getState().threadList[0].displayName).toBe('My renamed thread')
  })

  it('also updates the active thread displayName', () => {
    const thread = makeThread('t1', { displayName: 'Old name' })
    useThreadStore.getState().setActiveThread(thread)
    useThreadStore.getState().renameThread('t1', 'New name')
    expect(useThreadStore.getState().activeThread?.displayName).toBe('New name')
  })
})

describe('threadStore goal snapshots', () => {
  it('stores thread/goal/updated snapshots on the list and active thread', () => {
    const goal = makeGoal('t1')
    useThreadStore.getState().setThreadList([makeThreadSummary('t1')])
    useThreadStore.getState().setActiveThread(makeThread('t1'))

    useThreadStore.getState().setThreadGoal(goal)

    expect(useThreadStore.getState().goalSnapshots.get('t1')).toEqual(goal)
    expect(useThreadStore.getState().threadList[0].goal).toEqual(goal)
    expect(useThreadStore.getState().activeThread?.goal).toEqual(goal)
  })

  it('clears thread/goal/cleared snapshots on the list and active thread', () => {
    const goal = makeGoal('t1')
    useThreadStore.getState().setThreadList([makeThreadSummary('t1', { goal })])
    useThreadStore.getState().setActiveThread(makeThread('t1', { goal }))

    useThreadStore.getState().clearThreadGoal('t1')

    expect(useThreadStore.getState().goalSnapshots.has('t1')).toBe(false)
    expect(useThreadStore.getState().threadList[0].goal).toBeNull()
    expect(useThreadStore.getState().activeThread?.goal).toBeNull()
  })
})

describe('threadStore.setActiveThread', () => {
  it('does not change activeThreadId when loading a different thread id (stale read guard)', () => {
    useThreadStore.getState().setActiveThreadId('A')
    const threadB = makeThread('B', { displayName: 'Loaded B' })
    useThreadStore.getState().setActiveThread(threadB)
    expect(useThreadStore.getState().activeThreadId).toBe('A')
    expect(useThreadStore.getState().activeThread?.id).toBe('B')
  })

  it('preserves turns while clearing stale worktree metadata from local snapshots', () => {
    const worktreeThread = makeWorktreeThread('t1', {
      turns: [{ id: 'turn-1', status: 'completed', createdAt: '2026-01-01T00:00:00Z' }]
    })
    const localThread = makeThread('t1', {
      workspacePath: '/test/workspace',
      effectiveWorkspacePath: '/test/workspace',
      turns: []
    })

    useThreadStore.getState().setActiveThread(worktreeThread)
    useThreadStore.getState().setActiveThread(localThread)

    expect(useThreadStore.getState().activeThread?.worktree).toBeNull()
    expect(useThreadStore.getState().activeThread?.effectiveWorkspacePath).toBe('/test/workspace')
    expect(useThreadStore.getState().activeThread?.turns).toHaveLength(1)
  })
})

describe('selectFilteredThreads', () => {
  const threads = [
    makeThreadSummary('t1', { displayName: 'Hello World' }),
    makeThreadSummary('t2', { displayName: 'Goodbye Planet' }),
    makeThreadSummary('t3', { displayName: null })
  ]

  beforeEach(() => {
    useThreadStore.getState().setThreadList(threads)
  })

  function getFiltered(): ThreadSummary[] {
    return selectFilteredThreads(useThreadStore.getState())
  }

  it('returns all threads when searchQuery is empty', () => {
    useThreadStore.getState().setSearchQuery('')
    expect(getFiltered()).toHaveLength(3)
  })

  it('filters case-insensitively by displayName', () => {
    useThreadStore.getState().setSearchQuery('hello')
    expect(getFiltered().map((t) => t.id)).toEqual(['t1'])
  })

  it('returns empty array when no match', () => {
    useThreadStore.getState().setSearchQuery('zzz-no-match')
    expect(getFiltered()).toHaveLength(0)
  })

  it('handles null displayName gracefully (treats as empty string)', () => {
    useThreadStore.getState().setSearchQuery('null')
    // Thread t3 has null displayName; empty string does not include 'null'
    expect(getFiltered()).toHaveLength(0)
  })

  it('matches whitespace-only query as empty (no trim issue)', () => {
    useThreadStore.getState().setSearchQuery('   ')
    // '   '.trim() is '' so all threads returned
    expect(getFiltered()).toHaveLength(3)
  })
})

describe('threadStore full CRUD lifecycle', () => {
  // Helper to always get latest state snapshot
  const s = () => useThreadStore.getState()

  it('simulates create → select → rename → archive → delete flow', () => {
    // Create
    const t = makeThreadSummary('lifecycle-1')
    s().addThread(t)
    expect(s().threadList).toHaveLength(1)

    // Select
    s().setActiveThreadId('lifecycle-1')
    expect(s().activeThreadId).toBe('lifecycle-1')

    // Load full thread
    const full = makeThread('lifecycle-1')
    s().setActiveThread(full)
    expect(s().activeThread?.id).toBe('lifecycle-1')

    // Rename is client-side only in this test.
    s().renameThread('lifecycle-1', 'Renamed Thread')
    expect(s().activeThread?.displayName).toBe('Renamed Thread')
    expect(s().threadList[0].displayName).toBe('Renamed Thread')

    // Archive
    s().updateThreadStatus('lifecycle-1', 'archived')
    expect(s().activeThread?.status).toBe('archived')

    // Delete
    s().removeThread('lifecycle-1')
    expect(s().threadList).toHaveLength(0)
    expect(s().activeThreadId).toBeNull()
    expect(s().activeThread).toBeNull()
  })
})

describe('threadStore indicator state', () => {
  it('parks and consumes approvals by thread id', () => {
    const s = useThreadStore.getState()
    s.parkApproval('t1', {
      bridgeId: 'bridge-1',
      turnId: 'turn-1',
      rawParams: { threadId: 't1', turnId: 'turn-1' }
    })
    expect(useThreadStore.getState().parkedApprovals.has('t1')).toBe(true)

    const parked = useThreadStore.getState().consumeParkedApproval('t1')
    expect(parked?.bridgeId).toBe('bridge-1')
    expect(useThreadStore.getState().parkedApprovals.has('t1')).toBe(false)
  })

  it('parks multiple approvals for the same thread and consumes them together', () => {
    const s = useThreadStore.getState()
    s.parkApproval('t1', {
      bridgeId: 'bridge-1',
      turnId: 'turn-1',
      rawParams: { threadId: 't1', turnId: 'turn-1', requestId: 'req-1' }
    })
    s.parkApproval('t1', {
      bridgeId: 'bridge-2',
      turnId: 'turn-1',
      rawParams: { threadId: 't1', turnId: 'turn-1', requestId: 'req-2' }
    })
    s.parkApproval('t1', {
      bridgeId: 'bridge-2-replay',
      turnId: 'turn-1',
      rawParams: { threadId: 't1', turnId: 'turn-1', requestId: 'req-2' }
    })

    expect(useThreadStore.getState().parkedApprovals.get('t1')?.map((approval) => approval.bridgeId)).toEqual([
      'bridge-1',
      'bridge-2-replay'
    ])

    const parked = useThreadStore.getState().consumeParkedApprovals('t1')
    expect(parked.map((approval) => approval.bridgeId)).toEqual(['bridge-1', 'bridge-2-replay'])
    expect(useThreadStore.getState().parkedApprovals.has('t1')).toBe(false)
  })

  it('parks and consumes user input requests by thread id', () => {
    const s = useThreadStore.getState()
    s.parkUserInput('t1', {
      bridgeId: 'bridge-input',
      turnId: 'turn-1',
      rawParams: {
        threadId: 't1',
        turnId: 'turn-1',
        requestId: 'req-1',
        questions: []
      }
    })
    expect(useThreadStore.getState().parkedUserInputs.has('t1')).toBe(true)

    const parked = useThreadStore.getState().consumeParkedUserInput('t1')
    expect(parked?.bridgeId).toBe('bridge-input')
    expect(parked?.rawParams.requestId).toBe('req-1')
    expect(useThreadStore.getState().parkedUserInputs.has('t1')).toBe(false)
  })

  it('tracks and clears pending plan/unread-completed on thread activation', () => {
    const s = useThreadStore.getState()
    s.applyRuntimeSnapshot('thread-a', {
      running: false,
      waitingOnApproval: true,
      waitingOnPlanConfirmation: false
    }, {
      isActive: false,
      isDesktopOrigin: true
    })
    s.markPlanConfirmationPending('thread-a')
    s.markUnreadCompleted('thread-a')
    expect(useThreadStore.getState().pendingPlanConfirmationThreadIds.has('thread-a')).toBe(true)
    expect(useThreadStore.getState().pendingApprovalThreadIds.has('thread-a')).toBe(true)
    expect(useThreadStore.getState().unreadCompletedThreadIds.has('thread-a')).toBe(true)

    s.setActiveThreadId('thread-a')
    expect(useThreadStore.getState().pendingApprovalThreadIds.has('thread-a')).toBe(false)
    expect(useThreadStore.getState().pendingPlanConfirmationThreadIds.has('thread-a')).toBe(false)
    expect(useThreadStore.getState().unreadCompletedThreadIds.has('thread-a')).toBe(false)
  })

  it('applies runtime snapshots for running, approval, plan, and unread state', () => {
    const s = useThreadStore.getState()
    s.setThreadList([makeThreadSummary('thread-a', { originChannel: 'dotcraft-desktop' })])

    s.applyRuntimeSnapshot('thread-a', {
      running: true,
      waitingOnApproval: true,
      waitingOnPlanConfirmation: false
    }, {
      isActive: false,
      isDesktopOrigin: true
    })

    expect(useThreadStore.getState().runningTurnThreadIds.has('thread-a')).toBe(true)
    expect(useThreadStore.getState().pendingApprovalThreadIds.has('thread-a')).toBe(true)
    expect(useThreadStore.getState().unreadCompletedThreadIds.has('thread-a')).toBe(false)

    s.applyRuntimeSnapshot('thread-a', {
      running: false,
      waitingOnApproval: false,
      waitingOnPlanConfirmation: true
    }, {
      isActive: false,
      isDesktopOrigin: true
    })

    expect(useThreadStore.getState().runningTurnThreadIds.has('thread-a')).toBe(false)
    expect(useThreadStore.getState().pendingApprovalThreadIds.has('thread-a')).toBe(false)
    expect(useThreadStore.getState().pendingPlanConfirmationThreadIds.has('thread-a')).toBe(true)
    expect(useThreadStore.getState().unreadCompletedThreadIds.has('thread-a')).toBe(true)
  })
})

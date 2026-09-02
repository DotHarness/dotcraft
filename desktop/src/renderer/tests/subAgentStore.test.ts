import { beforeEach, describe, expect, it, vi } from 'vitest'
import { isSubAgentChildRunning, useSubAgentStore, type SubAgentChild } from '../stores/subAgentStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'

const appServerSendRequest = vi.fn()

function createDeferred<T>(): { promise: Promise<T>; resolve: (value: T) => void } {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((resolvePromise) => {
    resolve = resolvePromise
  })
  return { promise, resolve }
}

function makeRunningChild(overrides: Partial<SubAgentChild> = {}): SubAgentChild {
  return {
    childThreadId: 'child-1',
    parentThreadId: 'parent-1',
    nickname: 'Lovelace',
    agentRole: null,
    profileName: 'native',
    runtimeType: 'native',
    supportsSendInput: true,
    supportsResume: true,
    supportsClose: true,
    status: 'open',
    lastToolDisplay: null,
    lastMessagePreview: null,
    currentTool: null,
    inputTokens: 0,
    outputTokens: 0,
    isCompleted: false,
    runtime: { running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false },
    ...overrides
  }
}

describe('subAgentStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useSubAgentStore.getState().reset()
    useConnectionStore.getState().reset()
    useConnectionStore.setState({ capabilities: { subAgentSessions: true } })
    useThreadStore.getState().reset()
    vi.stubGlobal('window', {
      api: {
        appServer: { sendRequest: appServerSendRequest }
      }
    })
  })

  it('does not request child sessions when the server capability is unavailable', async () => {
    useConnectionStore.setState({ capabilities: { subAgentSessions: false } })

    await useSubAgentStore.getState().fetchChildren('parent-1')

    expect(appServerSendRequest).not.toHaveBeenCalled()
  })

  it('replaces and clears current turn metadata from runtime snapshots', () => {
    useSubAgentStore.getState().setChildren('parent-1', [makeRunningChild({
      runtime: {
        running: true,
        activeTurnId: 'turn-1',
        activeTurnStartedAt: '2026-08-24T00:00:00.000Z',
        waitingOnApproval: false,
        waitingOnPlanConfirmation: false
      }
    })])

    useSubAgentStore.getState().updateChildRuntime('child-1', {
      running: true,
      activeTurnId: 'turn-2',
      activeTurnStartedAt: '2026-08-24T01:00:00.000Z',
      waitingOnApproval: true,
      waitingOnPlanConfirmation: false
    })
    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')?.[0]?.runtime).toEqual(
      expect.objectContaining({
        activeTurnId: 'turn-2',
        activeTurnStartedAt: '2026-08-24T01:00:00.000Z'
      })
    )

    useSubAgentStore.getState().updateChildRuntime('child-1', {
      running: false,
      activeTurnId: null,
      activeTurnStartedAt: null,
      waitingOnApproval: false,
      waitingOnPlanConfirmation: false
    })
    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')?.[0]?.runtime).toEqual(
      expect.objectContaining({ activeTurnId: null, activeTurnStartedAt: null })
    )
  })

  it('does not let a delayed preview read overwrite current turn metadata', async () => {
    useSubAgentStore.getState().setChildren('parent-1', [makeRunningChild({
      runtime: {
        running: true,
        activeTurnId: 'turn-1',
        activeTurnStartedAt: '2026-08-24T00:00:00.000Z',
        waitingOnApproval: false,
        waitingOnPlanConfirmation: false
      }
    })])
    const staleTurns = createDeferred<unknown>()
    appServerSendRequest.mockImplementation((method: string) => {
      if (method === 'thread/turns/list') return staleTurns.promise
      if (method === 'thread/items/list') return Promise.resolve({ data: [], nextCursor: null })
      if (method === 'thread/read') return Promise.resolve({ thread: { id: 'child-1', turns: [] } })
      return Promise.resolve({})
    })

    const staleRead = useSubAgentStore.getState().fetchPreviews('parent-1', { force: true })
    useSubAgentStore.getState().updateChildRuntime('child-1', {
      running: true,
      activeTurnId: 'turn-2',
      activeTurnStartedAt: '2026-08-24T01:00:00.000Z',
      waitingOnApproval: false,
      waitingOnPlanConfirmation: false
    })

    staleTurns.resolve({
      data: [{ id: 'turn-1', status: 'running', startedAt: '2026-08-24T00:00:00.000Z' }],
      nextCursor: null
    })
    await staleRead

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')?.[0]?.runtime).toEqual(
      expect.objectContaining({
        activeTurnId: 'turn-2',
        activeTurnStartedAt: '2026-08-24T01:00:00.000Z'
      })
    )
  })

  it('loads child thread capability metadata from subagent/children/list', async () => {
    appServerSendRequest.mockResolvedValue({
      data: [
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-1',
            agentNickname: 'Lovelace',
            profileName: 'codex-cli',
            runtimeType: 'cli-oneshot',
            supportsSendInput: false,
            supportsResume: true,
            supportsClose: true,
            status: 'open'
          },
          thread: {
            id: 'child-1',
            displayName: 'Create hatch pet',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-05-03T00:00:00.000Z',
            lastActiveAt: '2026-05-03T00:01:00.000Z',
            runtime: {
              running: true,
              activeTurnId: 'turn-1',
              activeTurnStartedAt: '2026-05-03T00:00:30.000Z',
              waitingOnApproval: false,
              waitingOnPlanConfirmation: false
            }
          }
        }
      ]
    })

    await useSubAgentStore.getState().fetchChildren('parent-1')

    expect(appServerSendRequest).toHaveBeenCalledWith('subagent/children/list', {
      parentThreadId: 'parent-1',
      includeClosed: true,
      includeThreads: true
    })
    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')).toEqual([
      expect.objectContaining({
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        nickname: 'Create hatch pet',
        profileName: 'codex-cli',
        runtimeType: 'cli-oneshot',
        supportsSendInput: false,
        supportsResume: true,
        supportsClose: true,
        runtime: expect.objectContaining({
          running: true,
          activeTurnId: 'turn-1',
          activeTurnStartedAt: '2026-05-03T00:00:30.000Z'
        })
      })
    ])
  })

  it('falls back from displayName to nickname, taskName, agentPath segment, and childThreadId', async () => {
    appServerSendRequest.mockResolvedValue({
      data: [
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-display',
            agentNickname: 'Nick',
            taskName: 'task_one',
            agentPath: '/root/task_one',
            status: 'open'
          },
          thread: {
            id: 'child-display',
            displayName: 'Renamed child',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-05-03T00:00:00.000Z',
            lastActiveAt: '2026-05-03T00:01:00.000Z'
          }
        },
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-nick',
            agentNickname: 'Nick only',
            taskName: 'nick_only',
            agentPath: '/root/nick_only',
            status: 'open'
          }
        },
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-task',
            taskName: 'task_only',
            agentPath: '/root/task_only',
            status: 'open'
          }
        },
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-path',
            agentPath: '/root/path_only',
            status: 'open'
          }
        },
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-id',
            status: 'open'
          }
        }
      ]
    })

    await useSubAgentStore.getState().fetchChildren('parent-1')

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')?.map((child) => child.nickname)).toEqual([
      'Renamed child',
      'Nick only',
      'task_only',
      'path_only',
      'child-id'
    ])
  })

  it('normalizes role aliases from child list wire data', async () => {
    appServerSendRequest.mockResolvedValue({
      data: [
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-agent-type',
            agentNickname: 'Agent type child',
            agentType: 'explorer',
            status: 'open'
          },
          thread: {
            id: 'child-agent-type',
            displayName: 'Agent type child',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-05-03T00:00:00.000Z',
            lastActiveAt: '2026-05-03T00:01:00.000Z'
          }
        },
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-agent-snake',
            agentNickname: 'Agent snake child',
            agent_type: 'worker',
            status: 'open'
          },
          thread: {
            id: 'child-agent-snake',
            displayName: 'Agent snake child',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-05-03T00:00:00.000Z',
            lastActiveAt: '2026-05-03T00:01:00.000Z'
          }
        },
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-role',
            agentNickname: 'Role child',
            role: 'reviewer',
            status: 'open'
          },
          thread: {
            id: 'child-role',
            displayName: 'Role child',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-05-03T00:00:00.000Z',
            lastActiveAt: '2026-05-03T00:01:00.000Z'
          }
        },
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-source',
            status: 'open'
          },
          thread: {
            id: 'child-source',
            displayName: 'Source child',
            status: 'active',
            originChannel: 'subagent',
            source: {
              kind: 'subagent',
              subAgent: {
                agentNickname: 'Source child',
                agentType: 'explorer'
              }
            },
            createdAt: '2026-05-03T00:00:00.000Z',
            lastActiveAt: '2026-05-03T00:01:00.000Z'
          }
        }
      ]
    })

    await useSubAgentStore.getState().fetchChildren('parent-1')

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')).toEqual([
      expect.objectContaining({ childThreadId: 'child-agent-type', agentRole: 'explorer' }),
      expect.objectContaining({ childThreadId: 'child-agent-snake', agentRole: 'worker' }),
      expect.objectContaining({ childThreadId: 'child-role', agentRole: 'reviewer' }),
      expect.objectContaining({ childThreadId: 'child-source', agentRole: 'explorer' })
    ])
  })

  it('keeps completed child rows from closed child list results', async () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'codex-cli',
        runtimeType: 'cli-oneshot',
        supportsSendInput: false,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 7,
        outputTokens: 11,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])
    appServerSendRequest.mockResolvedValue({
      data: [
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-1',
            agentNickname: 'Lovelace',
            profileName: 'codex-cli',
            runtimeType: 'cli-oneshot',
            supportsSendInput: false,
            supportsResume: true,
            supportsClose: true,
            status: 'completed'
          },
          thread: {
            id: 'child-1',
            displayName: 'Create hatch pet',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-05-03T00:00:00.000Z',
            lastActiveAt: '2026-05-03T00:01:00.000Z',
            runtime: {
              running: false,
              waitingOnApproval: false,
              waitingOnPlanConfirmation: false
            }
          }
        },
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-2',
            agentNickname: 'Grace',
            supportsClose: true,
            status: 'failed'
          },
          thread: {
            id: 'child-2',
            displayName: 'Debug task',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-05-03T00:00:00.000Z',
            lastActiveAt: '2026-05-03T00:01:00.000Z',
            runtime: {
              running: false,
              waitingOnApproval: false,
              waitingOnPlanConfirmation: false
            }
          }
        }
      ]
    })

    await useSubAgentStore.getState().fetchChildren('parent-1')

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')).toEqual([
      expect.objectContaining({
        childThreadId: 'child-1',
        status: 'completed',
        currentTool: null,
        isCompleted: true,
        runtime: expect.objectContaining({ running: false })
      }),
      expect.objectContaining({
        childThreadId: 'child-2',
        status: 'failed',
        isCompleted: true,
        runtime: expect.objectContaining({ running: false })
      })
    ])

    useSubAgentStore.getState().updateProgress('parent-1', [
      {
        label: 'Lovelace',
        isCompleted: false,
        inputTokens: 99,
        outputTokens: 101,
        currentTool: 'ReadFile',
        currentToolDisplay: 'Reading stale output'
      }
    ])

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')?.[0]).toEqual(
      expect.objectContaining({
        currentTool: null,
        isCompleted: true,
        runtime: expect.objectContaining({ running: false })
      })
    )
  })

  it('creates placeholder rows from progress before child threads hydrate', () => {
    useSubAgentStore.getState().updateProgress('parent-1', [
      {
        label: 'Lovelace',
        isCompleted: false,
        inputTokens: 12,
        outputTokens: 34,
        currentTool: 'ReadFile',
        currentToolDisplay: 'Reading sprite atlas'
      }
    ])

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')).toEqual([
      expect.objectContaining({
        parentThreadId: 'parent-1',
        nickname: 'Lovelace',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 12,
        outputTokens: 34,
        supportsClose: false,
        isPlaceholder: true,
        isCompleted: false,
        runtime: expect.objectContaining({ running: true }),
        threadSummary: null
      })
    ])
  })

  it('clears completed placeholders from authoritative empty child lists while keeping running placeholders', async () => {
    useSubAgentStore.getState().updateProgress('parent-1', [
      {
        label: 'Finished',
        isCompleted: true,
        inputTokens: 1,
        outputTokens: 2,
        currentTool: null,
        currentToolDisplay: 'Completed'
      },
      {
        label: 'Still running',
        isCompleted: false,
        inputTokens: 3,
        outputTokens: 4,
        currentTool: 'ReadFile',
        currentToolDisplay: 'Reading sprite atlas'
      }
    ])
    appServerSendRequest.mockResolvedValue({ data: [] })

    await useSubAgentStore.getState().fetchChildren('parent-1')

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')).toEqual([
      expect.objectContaining({
        nickname: 'Still running',
        isPlaceholder: true,
        runtime: expect.objectContaining({ running: true })
      })
    ])
    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')?.some((child) => child.nickname === 'Finished')).toBe(false)
  })

  it('clears authoritative empty child lists and blocks stale progress placeholders', async () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 7,
        outputTokens: 11,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])
    appServerSendRequest.mockResolvedValue({ data: [] })

    await useSubAgentStore.getState().fetchChildren('parent-1', { authoritative: true })

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')).toEqual([])

    useSubAgentStore.getState().updateProgress('parent-1', [
      {
        label: 'Lovelace',
        isCompleted: false,
        inputTokens: 99,
        outputTokens: 101,
        currentTool: 'ReadFile',
        currentToolDisplay: 'Reading stale output'
      }
    ])

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')).toEqual([])
  })

  it('hydrates placeholder rows with real child threads while preserving progress display', async () => {
    useSubAgentStore.getState().updateProgress('parent-1', [
      {
        label: 'Lovelace',
        isCompleted: false,
        inputTokens: 12,
        outputTokens: 34,
        currentTool: 'ReadFile',
        currentToolDisplay: 'Reading sprite atlas'
      }
    ])
    appServerSendRequest.mockResolvedValue({
      data: [
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-1',
            agentNickname: 'Lovelace',
            agentRole: 'explorer',
            profileName: 'native',
            runtimeType: 'native',
            supportsSendInput: true,
            supportsResume: true,
            supportsClose: true,
            status: 'open'
          },
          thread: {
            id: 'child-1',
            displayName: 'Create hatch pet',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-05-03T00:00:00.000Z',
            lastActiveAt: '2026-05-03T00:01:00.000Z',
            runtime: {
              running: true,
              waitingOnApproval: false,
              waitingOnPlanConfirmation: false
            }
          }
        }
      ]
    })

    await useSubAgentStore.getState().fetchChildren('parent-1')

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')).toEqual([
      expect.objectContaining({
        childThreadId: 'child-1',
        nickname: 'Create hatch pet',
        agentRole: 'explorer',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 12,
        outputTokens: 34,
        supportsClose: true,
        isPlaceholder: false,
        runtime: expect.objectContaining({ running: true })
      })
    ])
    expect(useThreadStore.getState().threadList).toEqual([
      expect.objectContaining({
        id: 'child-1',
        displayName: 'Create hatch pet',
        originChannel: 'subagent',
        runtime: expect.objectContaining({ running: true })
      })
    ])
    expect(useThreadStore.getState().runningTurnThreadIds.has('child-1')).toBe(true)
  })

  it('lets terminal edge status override stale running runtime from cache', async () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 12,
        outputTokens: 34,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])
    appServerSendRequest.mockResolvedValue({
      data: [
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-1',
            agentNickname: 'Lovelace',
            supportsClose: true,
            status: 'closed'
          },
          thread: {
            id: 'child-1',
            displayName: 'Create hatch pet',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-05-03T00:00:00.000Z',
            lastActiveAt: '2026-05-03T00:01:00.000Z'
          }
        }
      ]
    })

    await useSubAgentStore.getState().fetchChildren('parent-1')

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')?.[0]).toEqual(
      expect.objectContaining({
        status: 'closed',
        currentTool: null,
        isCompleted: true,
        runtime: expect.objectContaining({ running: false })
      })
    )
  })

  it('uses child thread runtime to prevent open historical edges from showing as running after restart', async () => {
    appServerSendRequest.mockResolvedValue({
      data: [
        {
          edge: {
            parentThreadId: 'parent-1',
            childThreadId: 'child-1',
            agentNickname: 'Lovelace',
            supportsClose: true,
            status: 'open'
          },
          thread: {
            id: 'child-1',
            displayName: 'Create hatch pet',
            status: 'active',
            originChannel: 'subagent',
            createdAt: '2026-05-03T00:00:00.000Z',
            lastActiveAt: '2026-05-03T00:01:00.000Z',
            runtime: {
              running: false,
              waitingOnApproval: false,
              waitingOnPlanConfirmation: false
            }
          }
        }
      ]
    })

    await useSubAgentStore.getState().fetchChildren('parent-1')

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')?.[0]).toEqual(
      expect.objectContaining({
        status: 'open',
        currentTool: null,
        isCompleted: true,
        runtime: expect.objectContaining({ running: false })
      })
    )
  })

  it('does not treat hydrated children without runtime as running from open edge status alone', () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        nickname: 'Lovelace',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: null,
        currentTool: null,
        inputTokens: 0,
        outputTokens: 0,
        isCompleted: false,
        isPlaceholder: false
      }
    ])

    const child = useSubAgentStore.getState().childrenByParent.get('parent-1')?.[0]
    expect(child).toEqual(
      expect.objectContaining({
        isCompleted: false,
        status: 'open'
      })
    )
    expect(child ? isSubAgentChildRunning(child) : true).toBe(false)
  })

  it('merges progress descriptions and token usage into existing child rows', () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        nickname: 'Popper',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: null,
        currentTool: null,
        inputTokens: 0,
        outputTokens: 0,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])

    useSubAgentStore.getState().updateProgress('parent-1', [
      {
        label: 'Popper',
        isCompleted: false,
        inputTokens: 12,
        outputTokens: 34,
        currentTool: 'ReadFile',
        currentToolDisplay: 'Reading sprite atlas'
      }
    ])

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')?.[0]).toEqual(
      expect.objectContaining({
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 12,
        outputTokens: 34,
        isCompleted: false,
        runtime: expect.objectContaining({ running: true })
      })
    )
  })

  it('marks a child completed and clears current tool when runtime stops', () => {
    useSubAgentStore.getState().setChildren('parent-1', [
      {
        childThreadId: 'child-1',
        parentThreadId: 'parent-1',
        nickname: 'Popper',
        agentRole: null,
        profileName: 'native',
        runtimeType: 'native',
        supportsSendInput: true,
        supportsResume: true,
        supportsClose: true,
        status: 'open',
        lastToolDisplay: 'Reading sprite atlas',
        currentTool: 'ReadFile',
        inputTokens: 12,
        outputTokens: 34,
        isCompleted: false,
        runtime: {
          running: true,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])

    useSubAgentStore.getState().updateChildRuntime('child-1', {
      running: false,
      waitingOnApproval: false,
      waitingOnPlanConfirmation: false
    })

    expect(useSubAgentStore.getState().childrenByParent.get('parent-1')?.[0]).toEqual(
      expect.objectContaining({
        currentTool: null,
        isCompleted: true,
        runtime: expect.objectContaining({ running: false })
      })
    )
  })
})

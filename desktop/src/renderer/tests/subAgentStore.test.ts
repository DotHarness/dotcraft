import { beforeEach, describe, expect, it, vi } from 'vitest'
import { isSubAgentChildRunning, useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'

const appServerSendRequest = vi.fn()

describe('subAgentStore', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useSubAgentStore.getState().reset()
    useThreadStore.getState().reset()
    vi.stubGlobal('window', {
      api: {
        appServer: { sendRequest: appServerSendRequest }
      }
    })
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
        nickname: 'Lovelace',
        profileName: 'codex-cli',
        runtimeType: 'cli-oneshot',
        supportsSendInput: false,
        supportsResume: true,
        supportsClose: true,
        runtime: expect.objectContaining({ running: true })
      })
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
        task: 'Create hatch pet',
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
    expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(false)
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
        nickname: 'Lovelace',
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
        task: 'Create hatch pet',
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

  it('does not auto-expand running children after a user collapse', () => {
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
        isCompleted: true,
        runtime: {
          running: false,
          waitingOnApproval: false,
          waitingOnPlanConfirmation: false
        }
      }
    ])
    useSubAgentStore.getState().setParentCollapsed('parent-1', true)

    useSubAgentStore.getState().updateProgress('parent-1', [
      {
        label: 'Popper',
        task: 'Create hatch pet',
        isCompleted: false,
        inputTokens: 12,
        outputTokens: 34,
        currentTool: 'ReadFile',
        currentToolDisplay: 'Reading sprite atlas'
      }
    ])

    expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(true)
    expect(useSubAgentStore.getState().userCollapsedByParent.get('parent-1')).toBe(true)

    useSubAgentStore.getState().setParentCollapsed('parent-1', false)

    expect(useSubAgentStore.getState().collapsedByParent.get('parent-1')).toBe(false)
    expect(useSubAgentStore.getState().userCollapsedByParent.get('parent-1')).toBeUndefined()
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

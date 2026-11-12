import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { applyThreadNotification } from '../stores/threadNotifications'
import { useConnectionStore } from '../stores/connectionStore'
import { registerThreadConnection } from '../stores/threadConnectionStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { makeSubAgent } from './subAgentFixtures'
import { createThreadConversation, useConversationStore } from '../stores/conversationStore'
import { openThreadState, releaseThreadState } from '../stores/threadState'

const context = {
  locale: 'en' as const,
  foreground: true,
  updateWarmThread() {},
  updateWarmThreadMode: vi.fn(),
  releaseWarmThread() {},
  reloadThreadList: async () => {}
}

beforeEach(() => {
  context.updateWarmThreadMode.mockClear()
  releaseThreadState('thread-B')
  useConversationStore.getState().reset()
  useThreadStore.getState().reset()
  useSubAgentStore.getState().reset()
  useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: 'B', connectionId: 'B1' })
  useWorkspaceProjectsStore.getState().setPayload({
    foregroundWorkspacePath: 'B',
    secondaryLimit: 8,
    projects: [
      {
        path: 'A',
        name: 'A',
        state: 'secondary',
        loaded: true,
        running: true,
        threadCount: 1,
        threads: [],
        pinned: false,
        connectionKey: 'A',
        connectionId: 'A1'
      }
    ]
  })
  registerThreadConnection('parent-A', {
    threadId: 'parent-A',
    workspaceKey: 'a',
    connectionKey: 'A',
    connectionId: 'A1',
    capabilities: null,
    originChannel: 'dotcraft-desktop'
  })
  useSubAgentStore
    .getState()
    .setChildren('parent-A', [makeSubAgent({ parentThreadId: 'parent-A', childThreadId: 'child-A' })])
  useThreadStore.getState().applyRuntimeSnapshot('child-A', {
    running: true, waitingOnApproval: false, waitingOnPlanConfirmation: false
  }, { isDesktopOrigin: false })
})
afterEach(() => {
  releaseThreadState('parent-A')
  releaseThreadState('thread-B')
  useSubAgentStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
})

describe('confirmed thread settings notifications', () => {
  function selectForegroundThread(): void {
    openThreadState('thread-B')
    useThreadStore.getState().setActiveThreadId('thread-B')
    useThreadStore.getState().setActiveThread({
      id: 'thread-B', userId: 'local', workspacePath: 'B', status: 'active', originChannel: 'dotcraft-desktop',
      displayName: 'Thread B', metadata: {}, configuration: { mode: 'agent', model: 'test-model' },
      createdAt: '2026-11-12T00:00:00Z', lastActiveAt: '2026-11-12T00:00:00Z', turns: []
    })
  }

  it('updates active settings directly while preserving other configuration', () => {
    selectForegroundThread()
    applyThreadNotification({
      method: 'thread/settings/updated', connectionKey: 'B', connectionId: 'B1',
      params: { threadId: 'thread-B', mode: 'plan' }
    }, context)

    expect(useThreadStore.getState().activeThread?.configuration).toEqual({ mode: 'plan', model: 'test-model' })
    expect(useConversationStore.getState().threadMode).toBe('plan')
    expect(context.updateWarmThreadMode).toHaveBeenCalledWith('thread-B', 'plan')
  })

  it('updates a retained background conversation without changing the active one', () => {
    selectForegroundThread()
    const background = createThreadConversation('parent-A')
    applyThreadNotification({
      method: 'thread/settings/updated', connectionKey: 'A', connectionId: 'A1',
      params: { threadId: 'parent-A', mode: 'plan' }
    }, context)

    expect(background.getState().threadMode).toBe('plan')
    expect(useConversationStore.getState().threadMode).toBe('agent')
    expect(useThreadStore.getState().activeThread?.configuration?.mode).toBe('agent')
    expect(useThreadStore.getState().activeThreadId).toBe('thread-B')
    expect(context.updateWarmThreadMode).toHaveBeenCalledWith('parent-A', 'plan')
  })

  it('ignores settings from an old retained connection', () => {
    selectForegroundThread()
    const background = createThreadConversation('parent-A')
    applyThreadNotification({
      method: 'thread/settings/updated', connectionKey: 'A', connectionId: 'old',
      params: { threadId: 'parent-A', mode: 'plan' }
    }, context)

    expect(background.getState().threadMode).toBe('agent')
    expect(context.updateWarmThreadMode).not.toHaveBeenCalled()
  })

  it('rejects unsupported mode values', () => {
    selectForegroundThread()
    applyThreadNotification({ method: 'thread/settings/updated', params: { threadId: 'thread-B', mode: 'unknown' } }, context)
    expect(useConversationStore.getState().threadMode).toBe('agent')
    expect(useThreadStore.getState().activeThread?.configuration?.mode).toBe('agent')
    expect(context.updateWarmThreadMode).not.toHaveBeenCalled()
  })
})

describe('source-scoped conversation notifications', () => {
  it('updates a cached child runtime through its retained parent without changing foreground navigation', () => {
    expect(
      applyThreadNotification(
        {
          method: 'thread/status/changed',
          foreground: true,
          connectionKey: 'A',
          connectionId: 'A1',
          params: {
            threadId: 'child-A',
            runtime: {
              running: false,
              waitingOnApproval: true,
              waitingOnInput: false
            }
          }
        },
        context
      )
    ).toBe(true)
    expect(useThreadStore.getState().runtimeSnapshots.get('child-A')).toMatchObject({
      running: false,
      waitingOnApproval: true
    })
    expect(useThreadStore.getState().threadList).toEqual([])
    expect(useThreadStore.getState().activeThreadId).toBeNull()
  })

  it('ignores child state from an old connection lifetime', () => {
    applyThreadNotification(
      {
        method: 'thread/status/changed',
        foreground: true,
        connectionKey: 'A',
        connectionId: 'old',
        params: {
          threadId: 'child-A',
          runtime: { running: false, waitingOnApproval: true }
        }
      },
      context
    )
    expect(useThreadStore.getState().runtimeSnapshots.get('child-A')?.running).toBe(true)
  })
})

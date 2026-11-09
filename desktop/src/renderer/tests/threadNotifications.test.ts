import { afterEach, beforeEach, describe, expect, it } from 'vitest'
import { applyThreadNotification } from '../stores/threadNotifications'
import { useConnectionStore } from '../stores/connectionStore'
import { registerThreadConnection, releaseThreadConnection } from '../stores/threadConnectionStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { makeSubAgent } from './subAgentFixtures'

const context = {
  locale: 'en' as const,
  foreground: true,
  updateWarmThread() {},
  releaseWarmThread() {},
  reloadThreadList: async () => {}
}

beforeEach(() => {
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
})
afterEach(() => {
  releaseThreadConnection('parent-A')
  useSubAgentStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
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
    const child = useSubAgentStore.getState().childrenByParent.get('parent-A')?.[0]
    expect(child?.runtime).toMatchObject({
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
    expect(useSubAgentStore.getState().childrenByParent.get('parent-A')?.[0].runtime?.running).toBe(true)
  })
})

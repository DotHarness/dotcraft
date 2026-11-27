import { afterEach, describe, expect, it } from 'vitest'
import type { OwnedThreadRecord } from '../../shared/ownedThreads'
import {
  findThreadAcrossWorkspaces,
  startOwnedThreadActivityTracking,
  useOwnedThreadActivityStore
} from '../stores/ownedThreadActivityStore'
import { useOwnedThreadsStore } from '../stores/ownedThreadsStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { ThreadSummary } from '../types/thread'

const CONDUCTOR_RECORD: OwnedThreadRecord = {
  pluginId: 'oratorio',
  key: 'conductor',
  space: 'conductor',
  title: 'Oratorio',
  threadId: 'conductor-thread',
  workspacePath: '/chats',
  fingerprint: 'fp'
}

function threadSummary(id: string, readState?: ThreadSummary['readState']): ThreadSummary {
  return {
    id,
    displayName: 'Oratorio',
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: '2026-11-27T00:00:00.000Z',
    lastActiveAt: '2026-11-27T00:01:00.000Z',
    readState
  }
}

afterEach(() => {
  useOwnedThreadsStore.getState().setRecords([])
  useOwnedThreadActivityStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
  useThreadStore.setState({ threadList: [] })
})

describe('ownedThreadActivityStore', () => {
  it('resolves owned thread status from the Chats workspace when a project is foreground and counts unread revisions', () => {
    useOwnedThreadsStore.getState().setRecords([CONDUCTOR_RECORD])
    useThreadStore.setState({ threadList: [threadSummary('project-thread')] })
    const stop = startOwnedThreadActivityTracking()

    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/dotcraft',
      foregroundProjectId: 'dotcraft',
      secondaryLimit: 8,
      projects: [],
      chat: {
        projectId: 'chats',
        kind: 'local',
        path: '/chats',
        name: 'Chats',
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [
          threadSummary('conductor-thread', {
            firstUnread: { type: 'turn', turnId: 'turn-1' },
            revision: 'r1'
          })
        ]
      }
    })

    const resolved = findThreadAcrossWorkspaces(
      'conductor-thread',
      useThreadStore.getState().threadList,
      useWorkspaceProjectsStore.getState().chat?.threads as ThreadSummary[],
      useWorkspaceProjectsStore.getState().projects
    )
    expect(resolved?.id).toBe('conductor-thread')
    expect(useOwnedThreadActivityStore.getState().byThreadId['conductor-thread']?.count).toBe(1)

    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/dotcraft',
      foregroundProjectId: 'dotcraft',
      secondaryLimit: 8,
      projects: [],
      chat: {
        projectId: 'chats',
        kind: 'local',
        path: '/chats',
        name: 'Chats',
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [
          threadSummary('conductor-thread', {
            firstUnread: { type: 'turn', turnId: 'turn-1' },
            revision: 'r2'
          })
        ]
      }
    })
    expect(useOwnedThreadActivityStore.getState().byThreadId['conductor-thread']?.count).toBe(2)

    useWorkspaceProjectsStore.getState().setPayload({
      foregroundWorkspacePath: '/dotcraft',
      foregroundProjectId: 'dotcraft',
      secondaryLimit: 8,
      projects: [],
      chat: {
        projectId: 'chats',
        kind: 'local',
        path: '/chats',
        name: 'Chats',
        state: 'secondary',
        running: true,
        loaded: true,
        threadCount: 1,
        threads: [
          threadSummary('conductor-thread', {
            firstUnread: null,
            revision: 'r3'
          })
        ]
      }
    })
    expect(useOwnedThreadActivityStore.getState().byThreadId['conductor-thread']?.count).toBe(0)
    stop()
  })
})

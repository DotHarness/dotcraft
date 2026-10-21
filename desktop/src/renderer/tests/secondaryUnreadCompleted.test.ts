import { beforeEach, describe, expect, it } from 'vitest'
import type { WorkspaceProjectSummary } from '../../shared/workspaceProjects'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { ThreadSummary } from '../types/thread'

function thread(id: string, running: boolean): ThreadSummary {
  return {
    id,
    displayName: id,
    status: 'active',
    originChannel: 'dotcraft-desktop',
    createdAt: '2026-10-21T00:00:00Z',
    lastActiveAt: '2026-10-21T00:00:00Z',
    runtime: { running, busy: running }
  }
}

function project(state: WorkspaceProjectSummary['state'], threads: ThreadSummary[]): WorkspaceProjectSummary {
  return { projectId: 'd:/work/other', path: 'D:/work/other', name: 'other', state, running: true, loaded: true, threadCount: threads.length, threads, pinned: false, pinnedThreadIds: [] }
}

function publish(state: WorkspaceProjectSummary['state'], threads: ThreadSummary[]): void {
  useWorkspaceProjectsStore.getState().setPayload({ foregroundWorkspacePath: 'D:/work/main', projects: [project(state, threads)] })
}

const unread = () => useWorkspaceProjectsStore.getState().unreadCompletedByProject['d:/work/other'] ?? []

describe('unread completion in secondary projects', () => {
  beforeEach(() => {
    useWorkspaceProjectsStore.getState().reset()
    useThreadStore.getState().reset()
  })

  it('marks a thread that stops running and clears it when it runs again', () => {
    publish('secondary', [thread('a', true), thread('b', false)])
    publish('secondary', [thread('a', false), thread('b', false)])
    expect(unread()).toEqual(['a'])

    publish('secondary', [thread('a', true), thread('b', false)])
    expect(unread()).toEqual([])
  })

  it('carries the marks into the foreground list on promotion and back on demotion', () => {
    publish('secondary', [thread('a', true), thread('b', true)])
    publish('secondary', [thread('a', false), thread('b', false)])

    useThreadStore.getState().setThreadList([], 'D:/work/other')
    useThreadStore.getState().setThreadList([thread('a', false), thread('b', false)], 'D:/work/other')
    expect([...useThreadStore.getState().unreadCompletedThreadIds].sort()).toEqual(['a', 'b'])
    expect(unread()).toEqual([])

    useThreadStore.getState().setActiveThreadId('a')
    useThreadStore.getState().setThreadList([], 'D:/work/main')
    expect(unread()).toEqual(['b'])
  })
})

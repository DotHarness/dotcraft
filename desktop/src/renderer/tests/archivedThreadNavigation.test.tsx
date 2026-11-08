// @vitest-environment jsdom

import { act, renderHook, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import { useForegroundThreadList } from '../hooks/useForegroundThreadList'
import { useThreadRestoration } from '../hooks/useThreadRestoration'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { finishThreadRestoration } from '../stores/threadRestoration'
import { releaseThreadState } from '../stores/threadState'
import { selectFilteredThreads, useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { Thread, ThreadStatus } from '../types/thread'
import { openThread } from '../utils/desktopNavigation'
import { installDesktopApiMock } from './desktopApiMock'

const INITIAL_WORKSPACE = '/projects/one'

function thread(id: string, workspacePath: string, status: ThreadStatus = 'active'): Thread {
  return {
    id, workspacePath, status, displayName: id, originChannel: 'dotcraft-desktop',
    userId: 'local', metadata: {}, configuration: { mode: 'agent' }, turns: [],
    createdAt: '2026-11-08T00:00:00Z', lastActiveAt: '2026-11-08T00:00:00Z',
    runtime: { running: false, waitingOnApproval: false, waitingOnInput: false, waitingOnPlanConfirmation: false }
  }
}

beforeEach(() => {
  finishThreadRestoration()
  releaseThreadState('archived-target')
  useThreadStore.getState().reset()
  useConversationStore.getState().reset()
  useConnectionStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
  useUIStore.setState({ pendingProjectThreadOpen: null, pendingWelcomeTurn: null })
})

it.each([INITIAL_WORKSPACE, '/projects/two'])(
  'opens archived history in %s and keeps it readable after a normal list refresh',
  async (workspacePath) => {
    const archived = thread('archived-target', workspacePath, 'archived')
    const active = thread('active-target', workspacePath)
    const savedTurn = {
      id: 'saved-turn', threadId: archived.id, status: 'completed', itemsView: 'summary', itemCount: 1,
      startedAt: '2026-11-08T00:00:00Z', completedAt: '2026-11-08T00:01:00Z',
      items: [{ id: 'saved-message', type: 'agentMessage', status: 'completed', payload: { text: 'Saved answer' } }]
    }
    const sendRequest = vi.fn(async (method: string, params: { includeArchived?: boolean }) => {
      if (method === 'thread/list') return { data: params.includeArchived ? [active, archived] : [active] }
      if (method === 'thread/read') return { thread: archived }
      if (method === 'thread/turns/list') return { data: [savedTurn], nextCursor: null }
      return {}
    })
    const switchWorkspace = vi.fn()
    installDesktopApiMock({
      settings: { get: async () => ({}) },
      workspace: { switch: switchWorkspace },
      appServer: { sendRequest }
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: INITIAL_WORKSPACE, foregroundProjectId: INITIAL_WORKSPACE })
    useThreadStore.getState().setThreadList([thread('initial-active', INITIAL_WORKSPACE)], INITIAL_WORKSPACE)
    const project = { current: INITIAL_WORKSPACE }
    const protocol = { current: INITIAL_WORKSPACE }
    const hook = renderHook((path: string) => {
      const list = useForegroundThreadList(project, protocol)
      useThreadRestoration({ workspaceIdentity: path, workspaceKey: path, protocolWorkspacePath: path, locale: 'en' })
      return list
    }, { initialProps: INITIAL_WORKSPACE })
    switchWorkspace.mockImplementation(async (path: string) => {
      project.current = path
      protocol.current = path
      useThreadStore.getState().reset()
      useWorkspaceProjectsStore.setState({ foregroundWorkspacePath: path, foregroundProjectId: path })
      useConnectionStore.getState().setStatus({ status: 'connected' })
      hook.rerender(path)
      await hook.result.current.reloadThreadList()
    })

    await act(async () => { await openThread(archived.id, workspacePath, { includeArchived: true }) })

    await waitFor(() => expect(useThreadStore.getState().activeThread).toMatchObject({ id: archived.id, status: 'archived' }))
    expect(useConversationStore.getState().turns).toMatchObject([
      { id: savedTurn.id, items: [{ id: 'saved-message', text: 'Saved answer' }] }
    ])
    await act(async () => { await hook.result.current.reloadThreadList() })
    expect(selectFilteredThreads(useThreadStore.getState()).map((entry) => entry.id)).toEqual([active.id])
    expect(useThreadStore.getState().activeThread?.status).toBe('archived')
    expect(useThreadStore.getState().threadList.some((entry) => entry.id === archived.id)).toBe(true)
    expect(useConversationStore.getState().turns[0].items[0].text).toBe('Saved answer')
    expect(sendRequest.mock.calls.map(([method]) => method)).not.toContain('thread/resume')
    expect(sendRequest.mock.calls.map(([method]) => method)).not.toContain('thread/unarchive')
  }
)

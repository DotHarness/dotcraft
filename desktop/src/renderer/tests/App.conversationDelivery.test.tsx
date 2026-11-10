import { act, waitFor } from '@testing-library/react'
import type { RawNotificationPayload, RawServerRequestPayload } from '../../shared/appServerBoundary'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createThreadConversation, getThreadConversation, useConversationStore } from '../stores/conversationStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { makeRestoreResult } from './threadRestorationFixtures'
import { installApi, makeThread, makeThreadSummary, readyWorkspaceStatus, renderApp, flushPromises } from './appTestHarness'

describe('App conversation delivery', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useThreadStore.getState().reset()
    useConversationStore.getState().reset()
    useConnectionStore.getState().reset()
    useWorkspaceProjectsStore.getState().reset()
    useUIStore.setState({ activeMainView: 'conversation', pendingProjectThreadOpen: null })
  })
  it('keeps applying active conversation updates while hidden and reads nothing on restore', async () => {
    const threadId = 'thread-hidden'
    let notificationHandler: ((payload: RawNotificationPayload) => void) | undefined
    let serverRequestHandler: ((payload: RawServerRequestPayload) => void) | undefined
    let visibilityHandler: ((state: { minimized: boolean; visible: boolean; focused: boolean }) => void) | undefined
    const onNotification = vi.fn((handler: typeof notificationHandler) => {
      notificationHandler = handler
      return vi.fn()
    })
    const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
      serverRequestHandler = handler
      return vi.fn()
    })
    const onWindowVisibilityChanged = vi.fn((handler: typeof visibilityHandler) => {
      visibilityHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn(async (
      method: string,
      params?: { threadId?: string; includeTurns?: boolean }
    ) => {
      if (method === 'thread/resume') return makeRestoreResult(makeThread(params?.threadId ?? threadId, readyWorkspaceStatus.workspacePath))
      if (method === 'thread/read') {
        return {
          thread: makeThread(params?.threadId ?? threadId, readyWorkspaceStatus.workspacePath, 'Hidden thread')
        }
      }
      if (method === 'thread/list') return { data: [makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)] }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onNotification,
      onServerRequest,
      onWindowVisibilityChanged,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({
      status: 'connected', connectionKey: readyWorkspaceStatus.workspacePath, connectionId: 'connection-fixture'
    })
    useThreadStore.getState().setActiveThreadId(threadId)

    renderApp()

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/resume', expect.objectContaining({ threadId }))
    })
    await flushPromises()
    appServerSendRequest.mockClear()
    const agentDeltaSpy = vi.spyOn(useConversationStore.getState(), 'onAgentMessageDelta')

    await act(async () => {
      visibilityHandler?.({ minimized: true, visible: false, focused: false })
      notificationHandler?.({
        connectionKey: readyWorkspaceStatus.workspacePath, connectionId: 'connection-fixture',
        method: 'item/agentMessage/delta',
        params: { threadId, delta: 'hidden output' }
      })
      serverRequestHandler?.({
        bridgeId: 'bridge-approval-hidden',
        connectionKey: readyWorkspaceStatus.workspacePath, connectionId: 'connection-fixture',
        method: 'item/approval/request',
        params: {
          threadId,
          turnId: 'turn-approval',
          requestId: 'request-approval',
          itemId: 'item-approval',
          approvalType: 'shell',
          operation: 'npm test',
          target: readyWorkspaceStatus.workspacePath,
          reason: 'Run tests'
        }
      })
    })

    expect(agentDeltaSpy).toHaveBeenCalledWith('hidden output')
    expect(useConversationStore.getState().pendingApproval?.bridgeId).toBe('bridge-approval-hidden')
    expect(useThreadStore.getState().parkedApprovals.has(threadId)).toBe(false)
    appServerSendRequest.mockClear()

    await act(async () => {
      visibilityHandler?.({ minimized: false, visible: true, focused: true })
      window.dispatchEvent(new Event('focus'))
    })
    await flushPromises()

    expect(appServerSendRequest.mock.calls.filter((call) => call[0] === 'thread/read')).toHaveLength(0)
  })

  it('applies an event for a non-active thread with live state only to that thread', async () => {
    const activeThreadId = 'thread-active'
    const backgroundThreadId = 'thread-background'
    let notificationHandler: ((payload: RawNotificationPayload) => void) | undefined
    const onNotification = vi.fn((handler: typeof notificationHandler) => {
      notificationHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn(async (method: string, params?: { threadId?: string }) => {
      if (method === 'thread/resume') return makeRestoreResult(makeThread(params?.threadId ?? activeThreadId, readyWorkspaceStatus.workspacePath))
      if (method === 'thread/read') {
        return { thread: makeThread(params?.threadId ?? activeThreadId, readyWorkspaceStatus.workspacePath) }
      }
      if (method === 'thread/list') {
        return { data: [makeThreadSummary(activeThreadId, readyWorkspaceStatus.workspacePath)] }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onNotification,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({
      status: 'connected', connectionKey: readyWorkspaceStatus.workspacePath, connectionId: 'connection-fixture'
    })
    useThreadStore.getState().setActiveThreadId(activeThreadId)

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().activeThread?.id).toBe(activeThreadId)
    })
    await flushPromises()
    const background = createThreadConversation(backgroundThreadId)

    await act(async () => {
      for (const threadId of [backgroundThreadId, 'thread-without-state']) {
        notificationHandler?.({
          connectionKey: readyWorkspaceStatus.workspacePath, connectionId: 'connection-fixture',
          method: 'turn/started',
          params: { turn: { id: `turn-${threadId}`, threadId, status: 'running', items: [] } }
        })
        notificationHandler?.({
          connectionKey: readyWorkspaceStatus.workspacePath, connectionId: 'connection-fixture',
          method: 'thread/queue/updated',
          params: { threadId, queuedInputs: [{ id: `queued-${threadId}` }] }
        })
      }
    })

    expect(background.getState().turns.map((turn) => turn.id)).toEqual([`turn-${backgroundThreadId}`])
    expect(background.getState().turnStatus).toBe('running')
    expect(background.getState().queuedInputs).toHaveLength(1)
    expect(useConversationStore.getState().turns).toHaveLength(0)
    expect(useConversationStore.getState().turnStatus).toBe('idle')
    expect(useConversationStore.getState().queuedInputs).toHaveLength(0)
    expect(getThreadConversation('thread-without-state')).toBeUndefined()
  })
})

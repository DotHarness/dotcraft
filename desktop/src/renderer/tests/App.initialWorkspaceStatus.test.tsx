import { act, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.hoisted(() => {
  Object.defineProperty(globalThis, '__APP_VERSION__', {
    configurable: true,
    value: '0.1.6'
  })
})

import { useConnectionStore } from '../stores/connectionStore'
import { createThreadConversation, getThreadConversation, useConversationStore } from '../stores/conversationStore'
import { useGitStore } from '../stores/gitStore'
import { usePluginStore } from '../stores/pluginStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { WorkspaceStatusPayload } from '../../preload/api'
import type { WorkspaceProjectsPayload } from '../../shared/workspaceProjects'

import type { Thread, ThreadRuntimeSnapshot, ThreadSummary } from '../types/thread'

import { readyWorkspaceStatus, createDeferred, makeThreadSummary, makeThread, projectsPayloadFor, installApi, renderApp, flushPromises } from './appTestHarness'

describe('App initial workspace status bootstrap', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConnectionStore.getState().reset()
    useGitStore.getState().reset()
    useThreadStore.getState().reset()
    useConversationStore.getState().reset()
    useWorkspaceProjectsStore.getState().reset()
    usePluginStore.setState({
      plugins: [],
      diagnostics: [],
      loading: false,
      error: null,
      selectedPluginId: null,
      selectedPlugin: null,
      detailLoading: false
    })
    useUIStore.setState({
      activeMainView: 'conversation',
      pendingProjectThreadOpen: null,
      whatsNewOpenRequestSeq: 0,
      projectsSectionCollapsed: false,
      pinnedSectionCollapsed: false,
      chatsSectionCollapsed: false
    })

    Object.defineProperty(window, 'innerWidth', { configurable: true, value: 1200 })
    Object.defineProperty(window, 'innerHeight', { configurable: true, value: 800 })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reloads the foreground thread list when the workspace identity changes while connected', async () => {
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    let emitProjects: ((payload: WorkspaceProjectsPayload) => void) | null = null
    const workspaceBThreads = createDeferred<{ data: ThreadSummary[] }>()
    const appServerSendRequest = vi.fn((method: string, params?: { identity?: { workspacePath?: string } }) => {
      if (method === 'thread/list') {
        if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
          return workspaceBThreads.promise
        }
        return Promise.resolve({
          data: [makeThreadSummary('thread-a', readyWorkspaceStatus.workspacePath, 'A thread')]
        })
      }
      return Promise.resolve({})
    })
    const onWorkspaceStatusChange = vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
      emitStatus = callback
      return vi.fn()
    })
    const onProjectsChange = vi.fn((callback: (payload: WorkspaceProjectsPayload) => void) => {
      emitProjects = callback
      return vi.fn()
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      onWorkspaceStatusChange,
      onProjectsChange,
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-a'])
    })

    const callsAfterInitialLoad = appServerSendRequest.mock.calls.length
    act(() => {
      emitProjects?.(projectsPayloadFor(workspaceBStatus.workspacePath, 'B'))
    })
    await flushPromises()
    expect(appServerSendRequest.mock.calls.slice(callsAfterInitialLoad)).toHaveLength(0)

    act(() => {
      emitStatus?.(workspaceBStatus)
    })

    await waitFor(() => {
      expect(useThreadStore.getState().threadList).toEqual([])
      expect(useThreadStore.getState().threadListProjectKey).toBeNull()
    })
    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => {
        const params = call[1] as { identity?: { workspacePath?: string } } | undefined
        return call[0] === 'thread/list' && params?.identity?.workspacePath === workspaceBStatus.workspacePath
      })).toBe(true)
    })

    workspaceBThreads.resolve({
      data: [makeThreadSummary('thread-b', workspaceBStatus.workspacePath, 'B thread')]
    })

    await waitFor(() => {
      const state = useThreadStore.getState()
      expect(state.threadList.map((thread) => thread.id)).toEqual(['thread-b'])
      expect(state.threadListProjectKey).toBe('c:/sample/workspace-b')
    })
    for (const workspacePath of [readyWorkspaceStatus.workspacePath, workspaceBStatus.workspacePath]) {
      expect(appServerSendRequest.mock.calls.filter(([method, params]) =>
        method === 'thread/list' && params?.identity?.workspacePath === workspacePath
      )).toHaveLength(1)
    }
  })

  it('reloads when workspace status changes before the projects payload settles', async () => {
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    const appServerSendRequest = vi.fn(async (method: string, params?: { identity?: { workspacePath?: string } }) => {
      if (method !== 'thread/list') return {}
      if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
        return { data: [makeThreadSummary('thread-b', workspaceBStatus.workspacePath, 'B thread')] }
      }
      return { data: [makeThreadSummary('thread-a', readyWorkspaceStatus.workspacePath, 'A thread')] }
    })
    const onWorkspaceStatusChange = vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
      emitStatus = callback
      return vi.fn()
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      onWorkspaceStatusChange,
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-a'])
    })

    act(() => {
      emitStatus?.(workspaceBStatus)
    })

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => {
        const params = call[1] as { identity?: { workspacePath?: string } } | undefined
        return call[0] === 'thread/list' && params?.identity?.workspacePath === workspaceBStatus.workspacePath
      })).toBe(true)
    })
    await waitFor(() => {
      const state = useThreadStore.getState()
      expect(state.threadList.map((thread) => thread.id)).toEqual(['thread-b'])
      expect(state.threadListProjectKey).toBe('c:/sample/workspace-b')
    })
  })

  it('ignores a stale thread list response from the previous foreground workspace', async () => {
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    let emitProjects: ((payload: WorkspaceProjectsPayload) => void) | null = null
    const workspaceAThreads = createDeferred<{ data: ThreadSummary[] }>()
    const workspaceBThreads = createDeferred<{ data: ThreadSummary[] }>()
    const appServerSendRequest = vi.fn((method: string, params?: { identity?: { workspacePath?: string } }) => {
      if (method !== 'thread/list') return Promise.resolve({})
      if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
        return workspaceBThreads.promise
      }
      return workspaceAThreads.promise
    })
    const onWorkspaceStatusChange = vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
      emitStatus = callback
      return vi.fn()
    })
    const onProjectsChange = vi.fn((callback: (payload: WorkspaceProjectsPayload) => void) => {
      emitProjects = callback
      return vi.fn()
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      onWorkspaceStatusChange,
      onProjectsChange,
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => {
        const params = call[1] as { identity?: { workspacePath?: string } } | undefined
        return call[0] === 'thread/list' && params?.identity?.workspacePath === readyWorkspaceStatus.workspacePath
      })).toBe(true)
    })
    act(() => {
      useThreadStore.getState().setThreadList([
        makeThreadSummary('thread-a', readyWorkspaceStatus.workspacePath, 'A thread')
      ], readyWorkspaceStatus.workspacePath)
    })

    act(() => {
      emitStatus?.(workspaceBStatus)
      emitProjects?.(projectsPayloadFor(workspaceBStatus.workspacePath, 'B'))
    })

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => {
        const params = call[1] as { identity?: { workspacePath?: string } } | undefined
        return call[0] === 'thread/list' && params?.identity?.workspacePath === workspaceBStatus.workspacePath
      })).toBe(true)
    })
    workspaceBThreads.resolve({
      data: [makeThreadSummary('thread-b', workspaceBStatus.workspacePath, 'B thread')]
    })

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-b'])
    })

    workspaceAThreads.resolve({
      data: [makeThreadSummary('thread-a-late', readyWorkspaceStatus.workspacePath, 'Late A thread')]
    })
    await flushPromises()

    const state = useThreadStore.getState()
    expect(state.threadList.map((thread) => thread.id)).toEqual(['thread-b'])
    expect(state.threadListProjectKey).toBe('c:/sample/workspace-b')
  })

  it('opens a queued background project thread after its workspace becomes foreground', async () => {
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    let emitProjects: ((payload: WorkspaceProjectsPayload) => void) | null = null
    const appServerSendRequest = vi.fn(async (method: string, params?: { identity?: { workspacePath?: string }; threadId?: string }) => {
      if (method === 'thread/list') {
        if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
          return { data: [makeThreadSummary('thread-b', workspaceBStatus.workspacePath, 'B thread')] }
        }
        return { data: [makeThreadSummary('thread-a', readyWorkspaceStatus.workspacePath, 'A thread')] }
      }
      if (method === 'thread/read') {
        return { thread: makeThread(params?.threadId ?? 'thread-b', workspaceBStatus.workspacePath, 'B thread') }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      onWorkspaceStatusChange: vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
        emitStatus = callback
        return vi.fn()
      }),
      onProjectsChange: vi.fn((callback: (payload: WorkspaceProjectsPayload) => void) => {
        emitProjects = callback
        return vi.fn()
      }),
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-a'])
    })

    act(() => {
      useUIStore.getState().setPendingProjectThreadOpen({
        projectKey: workspaceBStatus.workspacePath,
        workspacePath: workspaceBStatus.workspacePath,
        threadId: 'thread-b'
      })
      emitStatus?.(workspaceBStatus)
      emitProjects?.(projectsPayloadFor(workspaceBStatus.workspacePath, 'B'))
    })

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-b'])
      expect(useThreadStore.getState().activeThreadId).toBe('thread-b')
      expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
    })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId: 'thread-b',
        includeTurns: true
      })
    })
  })

  it('clears a queued background project thread when the promoted list does not contain it', async () => {
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    let emitProjects: ((payload: WorkspaceProjectsPayload) => void) | null = null
    const appServerSendRequest = vi.fn(async (method: string, params?: { identity?: { workspacePath?: string } }) => {
      if (method === 'thread/list') {
        if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
          return { data: [makeThreadSummary('other-thread', workspaceBStatus.workspacePath, 'Other thread')] }
        }
        return { data: [makeThreadSummary('thread-a', readyWorkspaceStatus.workspacePath, 'A thread')] }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      onWorkspaceStatusChange: vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
        emitStatus = callback
        return vi.fn()
      }),
      onProjectsChange: vi.fn((callback: (payload: WorkspaceProjectsPayload) => void) => {
        emitProjects = callback
        return vi.fn()
      }),
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-a'])
    })

    act(() => {
      useUIStore.getState().setPendingProjectThreadOpen({
        projectKey: workspaceBStatus.workspacePath,
        workspacePath: workspaceBStatus.workspacePath,
        threadId: 'missing-thread'
      })
      emitStatus?.(workspaceBStatus)
      emitProjects?.(projectsPayloadFor(workspaceBStatus.workspacePath, 'B'))
    })

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['other-thread'])
      expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
    })
    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(appServerSendRequest.mock.calls.some((call) => {
      return call[0] === 'thread/read' && (call[1] as { threadId?: string } | undefined)?.threadId === 'missing-thread'
    })).toBe(false)
  })

  it('does not send the previous workspace thread subscription through a promoted connection', async () => {
    const sharedThreadId = 'shared-thread'
    const workspaceBStatus: WorkspaceStatusPayload = {
      ...readyWorkspaceStatus,
      workspacePath: 'C:\\sample\\workspace-b'
    }
    const workspaceBThreads = createDeferred<{ data: ThreadSummary[] }>()
    const delayedWorkspaceASubscribe = createDeferred<Record<string, never>>()
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    let activeWorkspace: 'a' | 'b' = 'a'
    let delayWorkspaceASubscribe = true
    const subscriptionRoutes: Array<{ method: string; threadId: string; workspace: 'a' | 'b' }> = []
    const appServerSendRequest = vi.fn(async (
      method: string,
      params?: { identity?: { workspacePath?: string }; threadId?: string }
    ) => {
      if (method === 'thread/list') {
        if (params?.identity?.workspacePath === workspaceBStatus.workspacePath) {
          return workspaceBThreads.promise
        }
        return { data: [makeThreadSummary(sharedThreadId, readyWorkspaceStatus.workspacePath, 'A thread')] }
      }
      if (method === 'thread/subscribe' || method === 'thread/unsubscribe') {
        subscriptionRoutes.push({
          method,
          threadId: params?.threadId ?? '',
          workspace: activeWorkspace
        })
        if (
          method === 'thread/subscribe' &&
          params?.threadId === sharedThreadId &&
          activeWorkspace === 'a' &&
          delayWorkspaceASubscribe
        ) {
          delayWorkspaceASubscribe = false
          return delayedWorkspaceASubscribe.promise
        }
        return {}
      }
      if (method === 'thread/read') {
        const workspace = activeWorkspace === 'b'
          ? workspaceBStatus.workspacePath
          : readyWorkspaceStatus.workspacePath
        return { thread: makeThread(params?.threadId ?? '', workspace) }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      onWorkspaceStatusChange: vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
        emitStatus = callback
        return vi.fn()
      }),
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual([sharedThreadId])
    })
    act(() => {
      useThreadStore.getState().setActiveThreadId(sharedThreadId)
    })
    await waitFor(() => {
      expect(subscriptionRoutes).toContainEqual({
        method: 'thread/subscribe',
        threadId: sharedThreadId,
        workspace: 'a'
      })
    })

    subscriptionRoutes.length = 0
    act(() => {
      useUIStore.getState().setPendingProjectThreadOpen({
        projectKey: workspaceBStatus.workspacePath,
        workspacePath: workspaceBStatus.workspacePath,
        threadId: sharedThreadId
      })
      activeWorkspace = 'b'
      emitStatus?.(workspaceBStatus)
      useConnectionStore.getState().setStatus({ status: 'connected' })
    })
    await flushPromises()

    expect(subscriptionRoutes).toEqual([])

    workspaceBThreads.resolve({
      data: [makeThreadSummary(sharedThreadId, workspaceBStatus.workspacePath, 'B thread')]
    })
    await waitFor(() => {
      expect(useThreadStore.getState().activeThreadId).toBe(sharedThreadId)
      expect(subscriptionRoutes).toEqual([{
        method: 'thread/subscribe',
        threadId: sharedThreadId,
        workspace: 'b'
      }])
    })

    delayedWorkspaceASubscribe.resolve({})
    await flushPromises()
    expect(useThreadStore.getState().activeThreadId).toBe(sharedThreadId)
    expect(subscriptionRoutes).toEqual([{
      method: 'thread/subscribe',
      threadId: sharedThreadId,
      workspace: 'b'
    }])

    subscriptionRoutes.length = 0
    act(() => {
      useUIStore.getState().setPendingProjectThreadOpen({
        projectKey: readyWorkspaceStatus.workspacePath,
        workspacePath: readyWorkspaceStatus.workspacePath,
        threadId: sharedThreadId
      })
      activeWorkspace = 'a'
      emitStatus?.(readyWorkspaceStatus)
      useConnectionStore.getState().setStatus({ status: 'connected' })
    })

    await waitFor(() => {
      expect(useThreadStore.getState().activeThreadId).toBe(sharedThreadId)
      expect(subscriptionRoutes).toEqual([{
        method: 'thread/subscribe',
        threadId: sharedThreadId,
        workspace: 'a'
      }])
    })
  })

  it('restores the active thread subscription when the foreground connection is refreshed', async () => {
    const appServerSendRequest = vi.fn(async (method: string, params?: { threadId?: string }) => {
      if (method === 'thread/list') {
        return { data: [makeThreadSummary('thread-pinned', readyWorkspaceStatus.workspacePath, 'Pinned thread')] }
      }
      if (method === 'thread/read') {
        return { thread: makeThread(params?.threadId ?? 'thread-pinned', readyWorkspaceStatus.workspacePath, 'Pinned thread') }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      settingsGet: vi.fn().mockResolvedValue({}),
      workspaceGetProjects: vi.fn().mockResolvedValue({
        ...projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'),
        projects: [
          {
            ...projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A').projects[0],
            pinnedThreadIds: ['thread-pinned']
          }
        ]
      })
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toContain('thread-pinned')
    })

    act(() => {
      useThreadStore.getState().setActiveThreadId('thread-pinned')
    })

    await waitFor(() => {
      expect(appServerSendRequest.mock.calls.some((call) => (
        call[0] === 'thread/subscribe' &&
        (call[1] as { threadId?: string; replayRecent?: boolean } | undefined)?.threadId === 'thread-pinned'
      ))).toBe(true)
    })

    appServerSendRequest.mockClear()
    act(() => {
      useConnectionStore.getState().setStatus({ status: 'connected' })
    })

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/subscribe', {
        threadId: 'thread-pinned',
        replayRecent: true
      })
    })
  })

  it('removes a stale active thread when thread/read reports not found', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    try {
      const appServerSendRequest = vi.fn(async (method: string, params?: { threadId?: string }) => {
        if (method === 'thread/list') {
          return { data: [makeThreadSummary('missing-thread', readyWorkspaceStatus.workspacePath, 'Missing thread')] }
        }
        if (method === 'thread/subscribe') {
          return {}
        }
        if (method === 'thread/read') {
          throw Object.assign(new Error(`Thread not found: ${params?.threadId ?? ''}`), { code: 'threadNotFound', rpcCode: -32010 })
        }
        return {}
      })
      installApi(readyWorkspaceStatus, {
        appServerSendRequest,
        modulesList: vi.fn().mockResolvedValue([]),
        modulesRunning: vi.fn().mockResolvedValue({}),
        settingsGet: vi.fn().mockResolvedValue({}),
        workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
      })
      useConnectionStore.getState().setStatus({ status: 'connected' })

      renderApp()

      await waitFor(() => {
        expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['missing-thread'])
      })

      act(() => {
        useThreadStore.getState().setActiveThreadId('missing-thread')
      })

      await waitFor(() => {
        expect(appServerSendRequest.mock.calls.some((call) => (
          call[0] === 'thread/read' &&
          (call[1] as { threadId?: string } | undefined)?.threadId === 'missing-thread'
        ))).toBe(true)
      })
      await waitFor(() => {
        const state = useThreadStore.getState()
        expect(state.activeThreadId).toBeNull()
        expect(state.threadList.map((thread) => thread.id)).toEqual([])
      })
    } finally {
      consoleError.mockRestore()
    }
  })

  it('keeps the thread and records the load error when thread/read fails for another reason', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    try {
      const appServerSendRequest = vi.fn(async (method: string, params?: { threadId?: string }) => {
        if (method === 'thread/list') {
          return { data: [makeThreadSummary('broken-thread', readyWorkspaceStatus.workspacePath, 'Broken thread')] }
        }
        if (method === 'thread/subscribe') {
          return {}
        }
        if (method === 'thread/read') {
          throw Object.assign(new Error('Thread history is unavailable.'), {
            rpcCode: -32095,
            data: { messageKey: 'errors.threadHistoryUnavailable', fallbackText: 'Thread history is unavailable.' }
          })
        }
        return {}
      })
      installApi(readyWorkspaceStatus, {
        appServerSendRequest,
        modulesList: vi.fn().mockResolvedValue([]),
        modulesRunning: vi.fn().mockResolvedValue({}),
        settingsGet: vi.fn().mockResolvedValue({}),
        workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
      })
      useConnectionStore.getState().setStatus({ status: 'connected' })

      renderApp()

      await waitFor(() => {
        expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['broken-thread'])
      })

      act(() => {
        useThreadStore.getState().setActiveThreadId('broken-thread')
      })

      await waitFor(() => {
        expect(appServerSendRequest.mock.calls.some((call) => (
          call[0] === 'thread/read' &&
          (call[1] as { threadId?: string } | undefined)?.threadId === 'broken-thread'
        ))).toBe(true)
      })
      await waitFor(() => {
        const state = useThreadStore.getState()
        expect(state.activeThreadId).toBe('broken-thread')
        expect(state.threadList.map((thread) => thread.id)).toEqual(['broken-thread'])
        expect(state.activeThreadLoadError).toEqual({ threadId: 'broken-thread', message: 'Thread history is unavailable.' })
      })
    } finally {
      consoleError.mockRestore()
    }
  })

  it('ignores secondary notifications even if their workspace path matches after normalization', async () => {
    let notificationHandler: ((payload: {
      method: string
      params?: unknown
      workspacePath?: string
      foreground?: boolean
    }) => void) | undefined
    const onNotification = vi.fn((handler: typeof notificationHandler) => {
      notificationHandler = handler
      return vi.fn()
    })
    installApi(readyWorkspaceStatus, {
      onNotification,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({}),
      appServerSendRequest: vi.fn(async (method: string) => method === 'thread/list' ? { data: [] } : {})
    })

    renderApp()
    await waitFor(() => {
      expect(onNotification).toHaveBeenCalled()
    })

    await act(async () => {
      notificationHandler?.({
        method: 'thread/started',
        workspacePath: 'F:\\examples\\workspace\\',
        foreground: false,
        params: {
          thread: {
            id: 'thread-secondary',
            displayName: 'Secondary thread',
            status: 'active',
            originChannel: 'dotcraft-desktop',
            createdAt: '2026-06-07T00:00:00.000Z',
            lastActiveAt: '2026-06-07T00:00:00.000Z'
          }
        }
      })
    })

    expect(useThreadStore.getState().threadList.map((thread) => thread.id)).not.toContain('thread-secondary')
  })

  it('keeps applying active conversation updates while hidden and reads nothing on restore', async () => {
    const threadId = 'thread-hidden'
    let notificationHandler: ((payload: { method: string; params?: unknown }) => void) | undefined
    let serverRequestHandler: ((payload: {
      bridgeId: string
      method: string
      params?: unknown
    }) => void) | undefined
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
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(threadId)

    renderApp()

    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/read', {
        threadId,
        includeTurns: true
      })
    })
    await flushPromises()
    appServerSendRequest.mockClear()
    const agentDeltaSpy = vi.spyOn(useConversationStore.getState(), 'onAgentMessageDelta')

    await act(async () => {
      visibilityHandler?.({ minimized: true, visible: false, focused: false })
      notificationHandler?.({
        method: 'item/agentMessage/delta',
        params: { threadId, delta: 'hidden output' }
      })
      serverRequestHandler?.({
        bridgeId: 'bridge-approval-hidden',
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

  it('does not let an initial thread restore snapshot release a request that arrived mid-read', async () => {
    const threadId = 'thread-input-initial-restore'
    const initialRead = createDeferred<{ thread: Thread }>()
    const reconcileRead = createDeferred<{ thread: Thread }>()
    let fullReadCount = 0
    let serverRequestHandler: ((payload: {
      bridgeId: string
      method: string
      params?: unknown
    }) => void) | undefined
    const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
      serverRequestHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn((
      method: string,
      params?: { threadId?: string; includeTurns?: boolean }
    ): Promise<unknown> => {
      if (method === 'thread/read' && params?.includeTurns === true) {
        fullReadCount += 1
        return fullReadCount === 1 ? initialRead.promise : reconcileRead.promise
      }
      if (method === 'thread/list') {
        return Promise.resolve({ data: [makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)] })
      }
      return Promise.resolve({})
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onServerRequest,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(threadId)

    renderApp()
    await waitFor(() => {
      expect(fullReadCount).toBe(1)
      expect(serverRequestHandler).toBeDefined()
    })

    await act(async () => {
      serverRequestHandler?.({
        bridgeId: 'bridge-input-initial-restore',
        method: 'item/tool/requestUserInput',
        params: {
          threadId,
          turnId: 'turn-initial-restore',
          requestId: 'request-initial-restore',
          isBlocking: false,
          questions: [{ id: 'confirm', question: 'Continue?', options: [{ label: 'Yes' }] }]
        }
      })
    })
    await waitFor(() => expect(fullReadCount).toBe(2))
    expect(useConversationStore.getState().pendingUserInput).toBeNull()

    const staleThread = makeThread(threadId, readyWorkspaceStatus.workspacePath, 'Stale initial restore')
    staleThread.turns = [{
      id: 'turn-stale-restore',
      threadId,
      status: 'completed',
      createdAt: '2026-06-07T00:00:00.000Z',
      items: [{ id: 'item-stale', type: 'agentMessage', status: 'completed', text: 'Stale preface' }]
    }]
    initialRead.resolve({ thread: staleThread })
    await flushPromises()
    expect(useConversationStore.getState().pendingUserInput).toBeNull()
    expect(useConversationStore.getState().turns[0]?.items[0]?.text).not.toBe('Stale preface')

    const latestThread = makeThread(threadId, readyWorkspaceStatus.workspacePath, 'Latest restore')
    latestThread.turns = [{
      id: 'turn-initial-restore',
      threadId,
      status: 'waitingInput',
      createdAt: '2026-06-07T00:00:00.000Z',
      items: [{ id: 'item-latest', type: 'agentMessage', status: 'completed', text: 'Latest preface' }]
    }]
    reconcileRead.resolve({ thread: latestThread })

    await waitFor(() => {
      expect(useConversationStore.getState().turns[0]?.items[0]?.text).toBe('Latest preface')
      expect(useConversationStore.getState().pendingUserInput?.bridgeId)
        .toBe('bridge-input-initial-restore')
    })
  })

  it('does not apply a pending-interaction reconcile after switching threads', async () => {
    const firstThreadId = 'thread-reconcile-old'
    const secondThreadId = 'thread-reconcile-new'
    let holdFirstThreadReconcile = false
    let resolveFirstThreadReconcile: ((value: { thread: Thread }) => void) | undefined
    let serverRequestHandler: ((payload: {
      bridgeId: string
      method: string
      params?: unknown
    }) => void) | undefined
    const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
      serverRequestHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn((
      method: string,
      params?: { threadId?: string }
    ): Promise<unknown> => {
      if (method === 'thread/read') {
        if (params?.threadId === firstThreadId && holdFirstThreadReconcile) {
          return new Promise((resolve) => {
            resolveFirstThreadReconcile = resolve
          })
        }
        return Promise.resolve({
          thread: makeThread(
            params?.threadId ?? firstThreadId,
            readyWorkspaceStatus.workspacePath,
            params?.threadId ?? firstThreadId
          )
        })
      }
      if (method === 'thread/list') {
        return Promise.resolve({
          data: [
            makeThreadSummary(firstThreadId, readyWorkspaceStatus.workspacePath),
            makeThreadSummary(secondThreadId, readyWorkspaceStatus.workspacePath)
          ]
        })
      }
      return Promise.resolve({})
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      onServerRequest,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(firstThreadId)

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().activeThread?.id).toBe(firstThreadId)
    })
    await flushPromises()
    holdFirstThreadReconcile = true

    await act(async () => {
      serverRequestHandler?.({
        bridgeId: 'bridge-reconcile-old',
        method: 'item/tool/requestUserInput',
        params: {
          threadId: firstThreadId,
          turnId: 'turn-old',
          requestId: 'request-old',
          isBlocking: false,
          questions: [{
            id: 'confirm',
            question: 'Continue?',
            options: [{ label: 'Yes' }]
          }]
        }
      })
    })
    await waitFor(() => {
      expect(resolveFirstThreadReconcile).toBeDefined()
    })

    await act(async () => {
      useThreadStore.getState().setActiveThreadId(secondThreadId)
    })
    await waitFor(() => {
      expect(useThreadStore.getState().activeThread?.id).toBe(secondThreadId)
    })

    const staleThread = makeThread(firstThreadId, readyWorkspaceStatus.workspacePath, 'Stale thread')
    staleThread.turns = [{
      id: 'turn-stale',
      threadId: firstThreadId,
      status: 'completed',
      createdAt: '2026-06-07T00:00:00.000Z',
      items: [{ id: 'item-stale', type: 'userMessage', status: 'completed', text: 'Stale history' }]
    }]
    await act(async () => {
      resolveFirstThreadReconcile?.({ thread: staleThread })
      await Promise.resolve()
    })

    expect(useThreadStore.getState().activeThread?.id).toBe(secondThreadId)
    expect(useConversationStore.getState().turns).toHaveLength(0)
  })

  it('preserves pending user input metadata across a thread switch', async () => {
    const firstThreadId = 'thread-input-old'
    const secondThreadId = 'thread-input-new'
    let firstThreadWaitingInput = false
    const appServerSendRequest = vi.fn(async (
      method: string,
      params?: { threadId?: string }
    ) => {
      if (method === 'thread/read') {
        const threadId = params?.threadId ?? firstThreadId
        const thread = makeThread(threadId, readyWorkspaceStatus.workspacePath, threadId)
        if (threadId === firstThreadId && firstThreadWaitingInput) {
          thread.runtime = {
            running: true,
            waitingOnApproval: false,
            waitingOnInput: true,
            waitingOnPlanConfirmation: false
          }
        }
        return {
          thread
        }
      }
      if (method === 'thread/list') {
        return {
          data: [
            makeThreadSummary(firstThreadId, readyWorkspaceStatus.workspacePath),
            makeThreadSummary(secondThreadId, readyWorkspaceStatus.workspacePath)
          ]
        }
      }
      return {}
    })
    installApi(readyWorkspaceStatus, {
      appServerSendRequest,
      settingsGet: vi.fn().mockResolvedValue({}),
      modulesList: vi.fn().mockResolvedValue([]),
      modulesRunning: vi.fn().mockResolvedValue({})
    })
    useConnectionStore.getState().setStatus({ status: 'connected' })
    useThreadStore.getState().setActiveThreadId(firstThreadId)

    renderApp()
    await waitFor(() => {
      expect(useThreadStore.getState().activeThread?.id).toBe(firstThreadId)
    })

    act(() => {
      useConversationStore.getState().onUserInputRequest('bridge-input-switch', {
        threadId: firstThreadId,
        turnId: 'turn-input-original',
        requestId: 'request-input-switch',
        isBlocking: true,
        questions: [{ id: 'confirm', question: 'Continue?', options: [{ label: 'Yes' }] }]
      })
      useConversationStore.setState({ activeTurnId: 'turn-transient' })
      firstThreadWaitingInput = true
    })

    await act(async () => {
      useThreadStore.getState().setActiveThreadId(secondThreadId)
    })
    await waitFor(() => {
      const parked = useThreadStore.getState().parkedUserInputs.get(firstThreadId)
      expect(parked?.turnId).toBe('turn-input-original')
      expect(parked?.rawParams.turnId).toBe('turn-input-original')
      expect(parked?.rawParams.isBlocking).toBe(true)
    })

    await act(async () => {
      useThreadStore.getState().setActiveThreadId(firstThreadId)
    })
    await waitFor(() => {
      const pending = useConversationStore.getState().pendingUserInput
      expect(pending?.bridgeId).toBe('bridge-input-switch')
      expect(pending?.turnId).toBe('turn-input-original')
      expect(pending?.isBlocking).toBe(true)
    })
  })
  it('applies an event for a non-active thread with live state only to that thread', async () => {
    const activeThreadId = 'thread-active'
    const backgroundThreadId = 'thread-background'
    let notificationHandler: ((payload: { method: string; params?: unknown }) => void) | undefined
    const onNotification = vi.fn((handler: typeof notificationHandler) => {
      notificationHandler = handler
      return vi.fn()
    })
    const appServerSendRequest = vi.fn(async (method: string, params?: { threadId?: string }) => {
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
    useConnectionStore.getState().setStatus({ status: 'connected' })
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
          method: 'turn/started',
          params: { turn: { id: `turn-${threadId}`, threadId, status: 'running', items: [] } }
        })
        notificationHandler?.({
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

async function renderWarmThreadsApp(
  threadIds: string[],
  workspace: {
    onWorkspaceStatusChange?: ReturnType<typeof vi.fn>
    onProjectsChange?: ReturnType<typeof vi.fn>
    workspaceGetProjects?: ReturnType<typeof vi.fn>
  } = {}
) {
  let notificationHandler: ((payload: { method: string; params?: unknown }) => void) | undefined
  let serverRequestHandler: ((payload: { bridgeId: string; method: string; params?: unknown }) => void) | undefined
  const onNotification = vi.fn((handler: typeof notificationHandler) => {
    notificationHandler = handler
    return vi.fn()
  })
  const onServerRequest = vi.fn((handler: typeof serverRequestHandler) => {
    serverRequestHandler = handler
    return vi.fn()
  })
  const legacyRequest = vi.fn(async (method: string, params?: { threadId?: string }) => {
    if (method === 'thread/read') {
      return { thread: makeThread(params?.threadId ?? threadIds[0], readyWorkspaceStatus.workspacePath) }
    }
    if (method === 'thread/list') {
      return { data: threadIds.map((threadId) => makeThreadSummary(threadId, readyWorkspaceStatus.workspacePath)) }
    }
    return {}
  })
  const { appServerSendRequest } = installApi(readyWorkspaceStatus, {
    appServerSendRequest: legacyRequest,
    onNotification,
    onServerRequest,
    settingsGet: vi.fn().mockResolvedValue({}),
    modulesList: vi.fn().mockResolvedValue([]),
    modulesRunning: vi.fn().mockResolvedValue({}),
    ...workspace
  })
  useConnectionStore.getState().setStatus({ status: 'connected' })
  renderApp()
  await waitFor(() => {
    expect(useThreadStore.getState().threadList).toHaveLength(threadIds.length)
  })

  const callsTo = (method: string, threadId?: string) => appServerSendRequest.mock.calls.filter((call) =>
    call[0] === method && (threadId == null || (call[1] as { threadId?: string } | undefined)?.threadId === threadId))

  return {
    appServerSendRequest,
    callsTo,
    notify: (method: string, params: unknown) => notificationHandler?.({ method, params }),
    request: (bridgeId: string, method: string, params: unknown) => serverRequestHandler?.({ bridgeId, method, params }),
    async open(threadId: string) {
      await act(async () => {
        useThreadStore.getState().setActiveThreadId(threadId)
      })
      await waitFor(() => {
        expect(useThreadStore.getState().activeThread?.id).toBe(threadId)
      })
      await flushPromises()
      await flushPromises()
    }
  }
}

function startedTurn(threadId: string, turnId: string) {
  return { turn: { id: turnId, threadId, status: 'running', items: [] } }
}

function completedTurn(threadId: string, turnId: string) {
  return { turn: { id: turnId, threadId, status: 'completed', items: [] } }
}

function runtimeChanged(threadId: string, runtime: Partial<ThreadRuntimeSnapshot>) {
  return {
    threadId,
    runtime: {
      running: false,
      waitingOnApproval: false,
      waitingOnInput: false,
      waitingOnPlanConfirmation: false,
      ...runtime
    }
  }
}

describe('warm threads', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useConnectionStore.getState().reset()
    useGitStore.getState().reset()
    useThreadStore.getState().reset()
    useConversationStore.getState().reset()
    useWorkspaceProjectsStore.getState().reset()
    useUIStore.setState({ activeMainView: 'conversation', pendingProjectThreadOpen: null })
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('reopens a warm thread from its live state without reading or resubscribing', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await act(async () => {
      app.notify('turn/started', startedTurn('thread-a', 'turn-a'))
      app.notify('turn/completed', completedTurn('thread-a', 'turn-a'))
    })
    await app.open('thread-b')

    expect(app.callsTo('thread/unsubscribe', 'thread-a')).toHaveLength(0)
    app.appServerSendRequest.mockClear()
    act(() => {
      useThreadStore.getState().setActiveThreadId('thread-a')
    })

    expect(useThreadStore.getState().activeThread?.id).toBe('thread-a')
    expect(useConversationStore.getState().turns.map((turn) => turn.id)).toEqual(['turn-a'])
    await flushPromises()
    for (const method of ['thread/read', 'thread/turns/list', 'thread/items/list', 'thread/subscribe']) {
      expect(app.callsTo(method)).toHaveLength(0)
    }
    expect(getThreadConversation('thread-b')).toBeDefined()
  })

  it('shows a turn that finished while the thread was warm', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await act(async () => { app.notify('turn/started', startedTurn('thread-a', 'turn-a')) })
    await app.open('thread-b')

    await act(async () => { app.notify('turn/completed', completedTurn('thread-a', 'turn-a')) })
    app.appServerSendRequest.mockClear()
    await app.open('thread-a')

    expect(useConversationStore.getState().turnStatus).toBe('idle')
    expect(useConversationStore.getState().turns.map((turn) => [turn.id, turn.status])).toEqual([['turn-a', 'completed']])
    expect(app.callsTo('thread/read', 'thread-a')).toHaveLength(0)
  })

  it('reconciles a reopened warm thread once when it missed its completion', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await act(async () => { app.notify('turn/started', startedTurn('thread-a', 'turn-a')) })
    await app.open('thread-b')
    await act(async () => { app.notify('thread/status/changed', runtimeChanged('thread-a', {})) })

    app.appServerSendRequest.mockClear()
    await app.open('thread-a')

    await waitFor(() => {
      expect(app.callsTo('thread/read', 'thread-a')).toHaveLength(1)
    })
    expect(app.callsTo('thread/subscribe')).toHaveLength(0)
  })

  it('makes an approval requested while warm actionable on reopen', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await act(async () => { app.notify('turn/started', startedTurn('thread-a', 'turn-a')) })
    await app.open('thread-b')

    await act(async () => {
      app.request('bridge-warm', 'item/approval/request', {
        threadId: 'thread-a',
        turnId: 'turn-a',
        requestId: 'request-warm',
        itemId: 'item-warm',
        approvalType: 'shell',
        operation: 'npm test',
        target: readyWorkspaceStatus.workspacePath,
        reason: 'Run tests'
      })
      app.notify('thread/status/changed', runtimeChanged('thread-a', { running: true, waitingOnApproval: true }))
    })
    expect(getThreadConversation('thread-a')?.getState().pendingApproval).toBeNull()

    app.appServerSendRequest.mockClear()
    await app.open('thread-a')

    expect(useConversationStore.getState().pendingApproval?.requestId).toBe('request-warm')
    expect(useConversationStore.getState().turnStatus).toBe('waitingApproval')
    expect(app.callsTo('thread/read')).toHaveLength(0)
  })

  it('releases the least recently viewed thread when an eleventh is left warm', async () => {
    const threadIds = Array.from({ length: 12 }, (_, index) => `thread-${index}`)
    const app = await renderWarmThreadsApp(threadIds)
    for (const threadId of threadIds.slice(0, 11)) await app.open(threadId)
    expect(app.callsTo('thread/unsubscribe')).toHaveLength(0)

    await app.open('thread-11')

    await waitFor(() => {
      expect(app.callsTo('thread/unsubscribe').map((call) => call[1])).toEqual([{ threadId: 'thread-0' }])
    })
    expect(getThreadConversation('thread-0')).toBeUndefined()
    for (const threadId of threadIds.slice(1)) expect(getThreadConversation(threadId)).toBeDefined()
  })

  it('releases a warm thread that has not been viewed for three hours', async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true, toFake: ['setInterval', 'clearInterval', 'Date'] })
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await app.open('thread-b')

    await act(async () => { vi.advanceTimersByTime(2 * 60 * 60 * 1000) })
    expect(getThreadConversation('thread-a')).toBeDefined()

    await act(async () => { vi.advanceTimersByTime(61 * 60 * 1000) })
    await waitFor(() => {
      expect(app.callsTo('thread/unsubscribe', 'thread-a')).toHaveLength(1)
    })
    expect(getThreadConversation('thread-a')).toBeUndefined()
    expect(getThreadConversation('thread-b')).toBeDefined()
  })

  it('releases every warm thread when the connection is refreshed', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b', 'thread-c'])
    await app.open('thread-a')
    await app.open('thread-b')
    await app.open('thread-c')
    app.appServerSendRequest.mockClear()

    act(() => {
      useConnectionStore.getState().setStatus({ status: 'connected' })
    })

    await waitFor(() => {
      expect(app.callsTo('thread/subscribe', 'thread-c')).toHaveLength(1)
    })
    expect(getThreadConversation('thread-a')).toBeUndefined()
    expect(getThreadConversation('thread-b')).toBeUndefined()
    expect(getThreadConversation('thread-c')).toBeDefined()
    expect(app.callsTo('thread/unsubscribe')).toHaveLength(0)
  })

  it('releases every warm thread when the foreground workspace switches', async () => {
    let emitStatus: ((payload: WorkspaceStatusPayload) => void) | null = null
    const onWorkspaceStatusChange = vi.fn((callback: (payload: WorkspaceStatusPayload) => void) => {
      emitStatus = callback
      return vi.fn()
    })
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'], {
      onWorkspaceStatusChange,
      workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(readyWorkspaceStatus.workspacePath, 'A'))
    })
    await app.open('thread-a')
    await app.open('thread-b')

    act(() => {
      emitStatus?.({ ...readyWorkspaceStatus, workspacePath: 'C:\\sample\\workspace-b' })
    })

    await waitFor(() => {
      expect(getThreadConversation('thread-a')).toBeUndefined()
      expect(getThreadConversation('thread-b')).toBeUndefined()
    })
  })

  it('releases a warm thread when it is archived', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await app.open('thread-b')

    await act(async () => { app.notify('thread/archived', { threadId: 'thread-a' }) })

    expect(getThreadConversation('thread-a')).toBeUndefined()
    await waitFor(() => {
      expect(app.callsTo('thread/unsubscribe', 'thread-a')).toHaveLength(1)
    })
  })

  it('releases a warm thread that another client rolled back', async () => {
    const app = await renderWarmThreadsApp(['thread-a', 'thread-b'])
    await app.open('thread-a')
    await app.open('thread-b')

    await act(async () => { app.notify('thread/rolledBack', { threadId: 'thread-a' }) })

    expect(getThreadConversation('thread-a')).toBeUndefined()
    await waitFor(() => {
      expect(app.callsTo('thread/unsubscribe', 'thread-a')).toHaveLength(1)
    })
  })
})

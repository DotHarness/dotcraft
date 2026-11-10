import { act, waitFor } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.hoisted(() => {
  Object.defineProperty(globalThis, '__APP_VERSION__', {
    configurable: true,
    value: '0.1.6'
  })
})

import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useGitStore } from '../stores/gitStore'
import { usePluginStore } from '../stores/pluginStore'
import { useThreadStore } from '../stores/threadStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import type { WorkspaceStatusPayload } from '../../preload/api'
import type { WorkspaceProjectsPayload } from '../../shared/workspaceProjects'

import type { ThreadSummary } from '../types/thread'

import { makeRestoreResult } from './threadRestorationFixtures'
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
    useConnectionStore.getState().setStatus({
      status: 'connected', connectionKey: readyWorkspaceStatus.workspacePath, connectionId: 'connection-a'
    })

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
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/list', expect.objectContaining({
      identity: expect.objectContaining({ workspacePath: workspaceBStatus.workspacePath })
    }))
    act(() => {
      useConnectionStore.getState().setStatus({
        status: 'connected', connectionKey: workspaceBStatus.workspacePath, connectionId: 'connection-b'
      })
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
    useConnectionStore.getState().setStatus({
      status: 'connected', connectionKey: readyWorkspaceStatus.workspacePath, connectionId: 'connection-a'
    })

    renderApp()

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-a'])
    })

    act(() => {
      emitStatus?.(workspaceBStatus)
    })
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/list', expect.objectContaining({
      identity: expect.objectContaining({ workspacePath: workspaceBStatus.workspacePath })
    }))
    act(() => {
      useConnectionStore.getState().setStatus({
        status: 'connected', connectionKey: workspaceBStatus.workspacePath, connectionId: 'connection-b'
      })
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
    useConnectionStore.getState().setStatus({
      status: 'connected', connectionKey: readyWorkspaceStatus.workspacePath, connectionId: 'connection-a'
    })

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
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/list', expect.objectContaining({
      identity: expect.objectContaining({ workspacePath: workspaceBStatus.workspacePath })
    }))
    act(() => {
      useConnectionStore.getState().setStatus({
        status: 'connected', connectionKey: workspaceBStatus.workspacePath, connectionId: 'connection-b'
      })
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
      if (method === 'thread/read') return { thread: makeThread(params?.threadId ?? 'thread-b', workspaceBStatus.workspacePath, 'B thread') }
      if (method === 'thread/resume') return makeRestoreResult(makeThread(params?.threadId ?? 'thread-b', workspaceBStatus.workspacePath, 'B thread'))
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
    useConnectionStore.getState().setStatus({
      status: 'connected', connectionKey: readyWorkspaceStatus.workspacePath, connectionId: 'connection-a'
    })

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
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/list', expect.objectContaining({
      identity: expect.objectContaining({ workspacePath: workspaceBStatus.workspacePath })
    }))
    act(() => {
      useConnectionStore.getState().setStatus({
        status: 'connected', connectionKey: workspaceBStatus.workspacePath, connectionId: 'connection-b'
      })
    })

    await waitFor(() => {
      expect(useThreadStore.getState().threadList.map((thread) => thread.id)).toEqual(['thread-b'])
      expect(useThreadStore.getState().activeThreadId).toBe('thread-b')
      expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
    })
    await waitFor(() => {
      expect(appServerSendRequest).toHaveBeenCalledWith('thread/resume', expect.objectContaining({ threadId: 'thread-b' }))
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
    useConnectionStore.getState().setStatus({
      status: 'connected', connectionKey: readyWorkspaceStatus.workspacePath, connectionId: 'connection-a'
    })

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
    expect(appServerSendRequest).not.toHaveBeenCalledWith('thread/list', expect.objectContaining({
      identity: expect.objectContaining({ workspacePath: workspaceBStatus.workspacePath })
    }))
    act(() => {
      useConnectionStore.getState().setStatus({
        status: 'connected', connectionKey: workspaceBStatus.workspacePath, connectionId: 'connection-b'
      })
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

})

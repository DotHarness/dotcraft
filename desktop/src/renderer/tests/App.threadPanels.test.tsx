import { act, waitFor } from '@testing-library/react'
import { beforeEach, expect, it, vi } from 'vitest'
import type { WorkspaceStatusPayload } from '../../shared/workspaceSetup'
import { useConnectionStore } from '../stores/connectionStore'
import { useConversationStore } from '../stores/conversationStore'
import { useDetailPanelStore } from '../stores/detailPanelStore'
import { useThreadStore } from '../stores/threadStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { makeRestoreResult } from './threadRestorationFixtures'
import { createDeferred, installApi, makeThread, makeThreadSummary, projectsPayloadFor, readyWorkspaceStatus, renderApp } from './appTestHarness'
beforeEach(() => {
  useDetailPanelStore.getState().reset()
  useConnectionStore.getState().reset()
  useConversationStore.getState().reset()
  useThreadStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
})
it('restores project A panels through B without interpreting the outgoing selection as a B thread', async () => {
  const a = readyWorkspaceStatus.workspacePath
  const b = 'C:\\sample\\workspace-b'
  const bThreads = createDeferred<{ data: ReturnType<typeof makeThreadSummary>[] }>()
  let current = a
  let statusChanged: ((payload: WorkspaceStatusPayload) => void) | undefined
  let projectsChanged: ((payload: ReturnType<typeof projectsPayloadFor>) => void) | undefined
  installApi(readyWorkspaceStatus, {
    settingsGet: vi.fn().mockResolvedValue({}),
    modulesList: vi.fn().mockResolvedValue([]),
    modulesRunning: vi.fn().mockResolvedValue({}),
    workspaceGetProjects: vi.fn().mockResolvedValue(projectsPayloadFor(a)),
    onWorkspaceStatusChange: vi.fn(callback => {
      statusChanged = callback
      return () => { }
    }),
    onProjectsChange: vi.fn(callback => {
      projectsChanged = callback
      return () => { }
    }),
    appServerSendRequest: vi.fn(async (method: string, params?: { threadId?: string; identity?: { workspacePath?: string } }) => {
      if (method === 'thread/list') return params?.identity?.workspacePath === b ? bThreads.promise : {
        data: [makeThreadSummary('thread-a', a)]
      }
      if (method === 'thread/resume') return makeRestoreResult(makeThread(params?.threadId ?? '', current))
      if (method === 'thread/read') return {
        thread: makeThread(params?.threadId ?? '', current)
      }
      return {}
    })
  })
  const sendRequest = vi.mocked(window.api.appServer.sendRequest)
  const read = vi.spyOn(window.api.workspace.viewer.panelState, 'read')
  const closeBrowser = vi.mocked(window.api.workspace.viewer.browser.destroy)
  const closeTerminal = vi.mocked(window.api.workspace.viewer.terminal.dispose)
  useConnectionStore.getState().setStatus({
    status: 'connected', connectionKey: a, connectionId: 'connection-a'
  })
  renderApp()
  await waitFor(() => expect(useThreadStore.getState().threadList).toHaveLength(1))
  act(() => useThreadStore.getState().setActiveThreadId('thread-a'))
  await waitFor(() => expect(useDetailPanelStore.getState().getCurrentScope()).toEqual({
    projectKey: 'c:/sample/workspace',
    threadId: 'thread-a'
  }))
  act(() => {
    const activeThread = useThreadStore.getState().activeThread!
    useThreadStore.setState({ activeThread: { ...activeThread, effectiveWorkspacePath: 'C:\\sample\\worktree-a' } })
  })
  expect(useDetailPanelStore.getState().currentWorkspacePath).toBe(a)
  expect(useDetailPanelStore.getState().getCurrentPanel().workspacePath).toBe('C:\\sample\\worktree-a')
  expect(useDetailPanelStore.getState().getCurrentPanel().browserWorkspacePath).toBe(a)
  let browser!: string, terminal!: string
  act(() => {
    const store = useDetailPanelStore.getState()
    browser = store.openBrowser({
      threadId: 'thread-a'
    })
    terminal = store.openTerminal({
      threadId: 'thread-a',
      cwd: a
    })
    store.setActiveDetailTab('plan')
    store.setDetailPanelFullView(true)
  })
  act(() => {
    current = b
    statusChanged?.({
      ...readyWorkspaceStatus,
      workspacePath: b
    })
    projectsChanged?.(projectsPayloadFor(b))
  })
  await waitFor(() => expect(useDetailPanelStore.getState().currentProjectKey).toBe('c:/sample/workspace-b'))
  expect(sendRequest.mock.calls.some(([method, params]) =>
    method === 'thread/list' && (params as { identity?: { workspacePath?: string } })?.identity?.workspacePath === b
  )).toBe(false)
  act(() => {
    useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: b, connectionId: 'connection-b' })
  })
  expect(read).not.toHaveBeenCalledWith({
    projectKey: 'c:/sample/workspace-b',
    threadId: 'thread-a'
  })
  bThreads.resolve({
    data: [makeThreadSummary('thread-b', b)]
  })
  await waitFor(() => expect(useThreadStore.getState().threadListProjectKey).toBe('c:/sample/workspace-b'))
  act(() => useThreadStore.getState().setActiveThreadId('thread-b'))
  await waitFor(() => expect(useDetailPanelStore.getState().getCurrentScope()?.threadId).toBe('thread-b'))
  const listRequestsBeforeReturn = sendRequest.mock.calls.filter(([method]) => method === 'thread/list').length
  act(() => {
    useDetailPanelStore.getState().setActiveDetailTab('board')
    current = a
    statusChanged?.(readyWorkspaceStatus)
    projectsChanged?.(projectsPayloadFor(a))
  })
  expect(sendRequest.mock.calls.filter(([method]) => method === 'thread/list')).toHaveLength(listRequestsBeforeReturn)
  act(() => {
    useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: a, connectionId: 'connection-a' })
  })
  await waitFor(() => expect(useThreadStore.getState().threadListProjectKey).toBe('c:/sample/workspace'))
  act(() => useThreadStore.getState().setActiveThreadId('thread-a'))
  await waitFor(() => expect(useDetailPanelStore.getState().getCurrentScope()?.threadId).toBe('thread-a'))
  expect(read).not.toHaveBeenCalledWith({
    projectKey: 'c:/sample/workspace',
    threadId: 'thread-b'
  })
  expect(useDetailPanelStore.getState().getCurrentPanel()).toMatchObject({
    activeDetailTab: {
      kind: 'system',
      id: 'plan'
    },
    openSystemTabs: ['plan'],
    detailPanelFullView: true
  })
  expect(useDetailPanelStore.getState().getCurrentPanel().tabs.map(tab => tab.id)).toEqual([browser, terminal])
  expect(closeBrowser).not.toHaveBeenCalled()
  expect(closeTerminal).not.toHaveBeenCalled()
})

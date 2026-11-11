import './setupPluginRuntime'
import { act, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { ConversationPanel } from '../components/layout/ConversationPanel'
import { LocaleProvider } from '../contexts/LocaleContext'
import { useForegroundThreadNavigation } from '../hooks/useForegroundThreadNavigation'
import { getThreadConversation, useConversationStore } from '../stores/conversationStore'
import { useConnectionStore } from '../stores/connectionStore'
import { useConfigStore } from '../stores/configStore'
import { usePluginStore } from '../stores/pluginStore'
import { useProvidersStore } from '../stores/providersStore'
import { useThreadStore } from '../stores/threadStore'
import { releaseThreadState } from '../stores/threadState'
import { finishThreadRestoration } from '../stores/threadRestoration'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { useTurnBookmarkStore } from '../stores/turnBookmarkStore'
import type { Thread } from '../types/thread'
import { installDesktopApiMock } from './desktopApiMock'
import { makeRestoreResult } from './threadRestorationFixtures'
import { openThread } from '../utils/desktopNavigation'

vi.mock('../components/conversation/ThreadHeader', () => ({ ThreadHeader: () => null }))
vi.mock('../components/conversation/MessageStream', () => ({ MessageStream: () => {
  const turns = useConversationStore((state) => state.turns)
  return <div role="log">{turns.flatMap(turn => turn.items.map(item => item.text))}</div>
} }))
const { submit } = vi.hoisted(() => ({ submit: vi.fn() }))
vi.mock('../components/conversation/InputComposer', () => ({
  InputComposer: ({ submitOverride }: { submitOverride?: () => void }) => <>
    <textarea aria-label="Draft" />
    <button aria-label="Send" onClick={() => (submitOverride ?? submit)()} />
  </>
}))
vi.mock('../components/conversation/RequestUserInputComposer', () => ({ RequestUserInputComposer: () => <div role="form" /> }))

function deferred<T>() {
  let resolve!: (value: T) => void
  let reject!: (reason: unknown) => void
  const promise = new Promise<T>((done, fail) => { resolve = done; reject = fail })
  return { promise, resolve, reject }
}

function thread(id: string, workspacePath: string, status: Thread['status'] = 'active'): Thread {
  return {
    id, workspacePath, displayName: id, status, originChannel: 'dotcraft-desktop', userId: 'local',
    metadata: {}, configuration: { mode: 'agent' },
    createdAt: '2026-11-10T00:00:00Z', lastActiveAt: '2026-11-10T00:00:00Z',
    turns: [{ id: `turn-${id}`, threadId: id, status: 'completed', startedAt: '2026-11-10T00:00:00Z',
      items: [{ id: `answer-${id}`, type: 'agentMessage', status: 'completed',
        payload: { text: `Answer ${id}` }, createdAt: '2026-11-10T00:00:00Z' }] }]
  } as Thread
}

const a = thread('thread-a', '/a')
const b = thread('thread-b', '/b')
const c = thread('thread-c', '/c')
const ids = [a.id, b.id, c.id, 'archived-target']
const sendRequest = vi.fn()
const sendConnectionRequest = vi.fn()
const settingsGet = vi.fn()

function connect(path: string, connectionId = `${path}-1`) {
  useConnectionStore.getState().setStatus({ status: 'connected', connectionKey: path, connectionId })
}

function projects(foreground: string) {
  useWorkspaceProjectsStore.getState().setPayload({
    foregroundWorkspacePath: foreground, foregroundProjectId: foreground, secondaryLimit: 8,
    projects: ['/a', '/b', '/c'].map(path => ({
      path, name: path, state: path === foreground ? 'foreground' : 'secondary', loaded: true,
      running: true, threadCount: 1, threads: [], pinned: false, connectionKey: path, connectionId: `${path}-1`
    }))
  })
}

function View({ path, projectKey = path }: { path: string; projectKey?: string }) {
  const { foregroundReady } = useForegroundThreadNavigation({
    projectKey, protocolWorkspacePath: path, remoteWorkspace: projectKey.startsWith('remote:'), locale: 'en'
  })
  return <LocaleProvider><ConversationPanel workspacePath={path} projectKey={projectKey} workspaceReady={foregroundReady} /></LocaleProvider>
}

beforeEach(() => {
  finishThreadRestoration()
  for (const id of ids) releaseThreadState(id)
  useThreadStore.getState().reset()
  useConnectionStore.getState().reset()
  useConversationStore.getState().reset()
  useWorkspaceProjectsStore.getState().reset()
  useTurnBookmarkStore.setState({ loaded: false, byThread: {} })
  useUIStore.setState({ pendingProjectThreadOpen: null, pendingWelcomeTurn: null,
    pendingThreadCreation: null, planApprovalDismissed: {}, activeMainView: 'conversation' })
  submit.mockReset()
  sendRequest.mockReset().mockImplementation(async method => method === 'thread/list' ? { data: [a, b, c] } : {})
  sendConnectionRequest.mockReset().mockImplementation(async (key, method, params, _timeout, options) => {
    const selected = key === '/a' ? a : key === '/b' ? b : c
    if (method === 'thread/resume') {
      const result = makeRestoreResult({ ...selected, id: params.threadId })
      options?.onResponse?.(result)
      return result
    }
    if (method === 'thread/read') return { thread: selected }
    if (method === 'thread/turns/list') return { data: selected.turns, nextCursor: null }
    return {}
  })
  settingsGet.mockReset().mockResolvedValue({})
  installDesktopApiMock({
    settings: { get: settingsGet, set: vi.fn().mockResolvedValue(undefined) },
    appServer: { sendRequest, sendConnectionRequest },
    file: { readFile: vi.fn().mockResolvedValue('{}') },
    shell: { listOpenTargets: vi.fn().mockResolvedValue([]) },
    workspace: { saveImageToTemp: vi.fn() }, voice: undefined
  })
  connect('/b')
  projects('/b')
})

afterEach(() => {
  finishThreadRestoration()
  vi.useRealTimers()
  vi.restoreAllMocks()
})

async function warmB() {
  const view = render(<View path="/b" />)
  await act(async () => { useThreadStore.getState().setActiveThreadId(b.id) })
  expect(screen.getByRole('log')).toHaveTextContent('Answer thread-b')
  const original = getThreadConversation(b.id)
  await act(async () => {
    projects('/a')
    connect('/a')
    view.rerender(<View path="/a" />)
  })
  await act(async () => { useThreadStore.getState().setActiveThreadId(a.id) })
  expect(screen.getByRole('log')).toHaveTextContent('Answer thread-a')
  return { view, original }
}

function pending(path = '/b', threadId = b.id) {
  return useUIStore.getState().setPendingProjectThreadOpen({ projectKey: path, workspacePath: path, threadId })!
}

describe('foreground warm navigation', () => {
  it.each(['workspace-first', 'connection-first'] as const)(
    'shows a warm conversation before list/settings resolve with %s notifications', async order => {
      const { view, original } = await warmB()
      const list = deferred<{ data: Thread[] }>()
      const settings = deferred<{}>()
      sendRequest.mockImplementation(method => method === 'thread/list' ? list.promise : Promise.resolve({}))
      settingsGet.mockReturnValue(settings.promise)
      useConfigStore.setState({ config: { ProviderId: 'provider-a' } })
      sendConnectionRequest.mockClear()
      act(() => { pending() })
      if (order === 'workspace-first') {
        act(() => { view.rerender(<View path="/b" />) })
      } else {
        act(() => { connect('/b') })
      }
      expect(useThreadStore.getState().activeThreadId).toBeNull()
      expect(sendConnectionRequest).not.toHaveBeenCalled()
      act(() => {
        if (order === 'workspace-first') connect('/b')
        else view.rerender(<View path="/b" />)
      })
      expect(screen.getByRole('log')).toHaveTextContent('Answer thread-b')
      expect(useConfigStore.getState().config?.ProviderId).not.toBe('provider-a')
      expect(getThreadConversation(b.id)).toBe(original)
      expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
      fireEvent.click(screen.getByRole('button', { name: 'Send' }))
      expect(submit).toHaveBeenCalledTimes(1)
      expect(sendConnectionRequest).not.toHaveBeenCalled()
      act(() => { projects('/b') })
      act(() => { connect('/b') })
      expect(screen.getByRole('log')).toHaveTextContent('Answer thread-b')
      expect(sendConnectionRequest).not.toHaveBeenCalled()
      await act(async () => { list.resolve({ data: [b] }); settings.resolve({}) })
      expect(screen.getByRole('log')).toHaveTextContent('Answer thread-b')
      expect(sendConnectionRequest).not.toHaveBeenCalled()
    }
  )

  it('keeps the warm view usable when background list and settings fail', async () => {
    const { view, original } = await warmB()
    vi.spyOn(console, 'error').mockImplementation(() => {})
    const list = deferred<never>()
    const settings = deferred<never>()
    sendRequest.mockImplementation(method => method === 'thread/list' ? list.promise : Promise.resolve({}))
    settingsGet.mockReturnValue(settings.promise)
    act(() => { pending(); connect('/b'); view.rerender(<View path="/b" />) })
    await act(async () => { list.reject(new Error('list unavailable')); settings.reject(new Error('settings unavailable')) })
    expect(screen.getByRole('log')).toHaveTextContent('Answer thread-b')
    expect(getThreadConversation(b.id)).toBe(original)
  })

  it('retains a parked question and loaded history without restoring again', async () => {
    const { view, original } = await warmB()
    act(() => {
      useThreadStore.getState().applyRuntimeSnapshot(b.id, {
        running: true, waitingOnApproval: false, waitingOnInput: true, waitingOnPlanConfirmation: false
      }, { isDesktopOrigin: true })
      useThreadStore.getState().parkUserInput(b.id, { bridgeId: 'bridge-b', turnId: 'turn-thread-b', rawParams: {
        threadId: b.id, turnId: 'turn-thread-b', requestId: 'question-b', isBlocking: true,
        questions: [{ id: 'choice', question: 'Continue?', options: [] }]
      } })
    })
    const turns = original!.getState().turns
    sendRequest.mockReturnValue(new Promise(() => {}))
    settingsGet.mockReturnValue(new Promise(() => {}))
    sendConnectionRequest.mockClear()
    act(() => { pending(); connect('/b'); view.rerender(<View path="/b" />) })
    expect(useConversationStore.getState().pendingUserInput?.requestId).toBe('question-b')
    expect(useConversationStore.getState().turns).toBe(turns)
    expect(sendConnectionRequest).not.toHaveBeenCalled()
  })

  it('does not turn a same-workspace archived read into project-list navigation', async () => {
    await warmB()
    const archived = thread('archived-target', '/a', 'archived')
    const read = deferred<{ thread: Thread }>()
    sendRequest.mockClear().mockImplementation(method =>
      method === 'thread/read' ? read.promise : Promise.resolve({ data: [a] }))
    sendConnectionRequest.mockClear().mockImplementation(async (_key, method) => {
      if (method === 'thread/read') return { thread: archived }
      if (method === 'thread/turns/list') return { data: archived.turns, nextCursor: null }
      return {}
    })
    let opening!: Promise<void>
    await act(async () => { opening = openThread(archived.id, undefined, { includeArchived: true }) })
    expect(useUIStore.getState().pendingProjectThreadOpen).toBeNull()
    expect(useThreadStore.getState().activeThreadId).toBe(a.id)
    expect(sendRequest.mock.calls.map(([method]) => method)).toEqual(['thread/read'])
    await act(async () => { read.resolve({ thread: archived }); await opening })
    expect(screen.getByRole('log')).toHaveTextContent('Answer archived-target')
    expect(sendConnectionRequest.mock.calls.some(([, method]) => method === 'thread/resume')).toBe(false)
    expect(sendRequest.mock.calls.some(([method]) => method === 'thread/list')).toBe(false)
  })

  it('does not reactivate B when a later C navigation supersedes it', async () => {
    const { view } = await warmB()
    const listB = deferred<{ data: Thread[] }>()
    sendRequest.mockReturnValue(listB.promise)
    act(() => { pending(); view.rerender(<View path="/b" />) })
    act(() => { pending('/c', c.id) })
    act(() => { connect('/b') })
    expect(useThreadStore.getState().activeThreadId).toBeNull()
    const listC = deferred<{ data: Thread[] }>()
    sendRequest.mockReturnValue(listC.promise)
    act(() => { connect('/c'); view.rerender(<View path="/c" />) })
    await act(async () => { listC.resolve({ data: [c] }) })
    expect(useThreadStore.getState().activeThreadId).toBe(c.id)
    await act(async () => { listB.resolve({ data: [b] }) })
    expect(useThreadStore.getState().activeThreadId).toBe(c.id)
    expect(screen.getByRole('log')).toHaveTextContent('Answer thread-c')
  })

  it('does not reuse a warm thread for another remote project with the same path and thread id', async () => {
    const { view } = await warmB()
    const remoteKey = 'remote:device:other:project'
    const list = deferred<{ data: Thread[] }>()
    sendRequest.mockReturnValue(list.promise)
    sendConnectionRequest.mockClear()
    act(() => {
      pending(remoteKey)
      connect(remoteKey)
      view.rerender(<View path="/b" projectKey={remoteKey} />)
    })
    expect(useThreadStore.getState().activeThreadId).toBeNull()
    expect(useUIStore.getState().pendingProjectThreadOpen?.projectKey).toBe(remoteKey)
    expect(sendConnectionRequest).not.toHaveBeenCalled()
    await act(async () => { list.resolve({ data: [b] }) })
    expect(sendConnectionRequest.mock.calls.some(([key, method]) => key === remoteKey && method === 'thread/resume')).toBe(true)
  })

  it('loads target catalogs after the connection and workspace agree without losing their late responses', async () => {
    const { view } = await warmB()
    const plugins = deferred<{}>()
    const providers = deferred<{}>()
    sendRequest.mockClear().mockImplementation(method => {
      if (method === 'plugin/list') return plugins.promise
      return Promise.resolve(method === 'thread/list' ? { data: [b] } : {})
    })
    sendConnectionRequest.mockClear().mockImplementation((_key, method) =>
      method === 'provider/list' ? providers.promise : Promise.resolve({}))
    act(() => {
      pending()
      useConnectionStore.getState().setStatus({
        status: 'connected', connectionKey: '/b', connectionId: '/b-1',
        capabilities: { pluginManagement: true, providerManagement: true }
      })
    })
    expect(sendRequest.mock.calls.some(([method]) => method === 'plugin/list' || method === 'provider/list')).toBe(false)
    act(() => { view.rerender(<View path="/b" />) })
    expect(sendRequest.mock.calls.filter(([method]) => method === 'plugin/list')).toHaveLength(1)
    expect(sendConnectionRequest.mock.calls.filter(([, method]) => method === 'provider/list')).toHaveLength(1)
    await act(async () => {
      plugins.resolve({ plugins: [], marketplaces: [], diagnostics: [], snapshotRevision: 1 })
      providers.resolve({ providers: [{ id: 'provider-b', displayName: 'B', protocol: 'openai' }] })
    })
    expect(usePluginStore.getState().catalogStatus).toBe('ready')
    expect(useProvidersStore.getState().status).toBe('ready')
    expect(useProvidersStore.getState().providers[0].id).toBe('provider-b')
    expect(screen.getByRole('log')).toHaveTextContent('Answer thread-b')
  })

  it('never resumes the outgoing thread on a connection that becomes ready before its workspace status', async () => {
    const { view } = await warmB()
    sendConnectionRequest.mockClear()
    act(() => { useConnectionStore.getState().setStatus({ status: 'connecting' }) })
    sendConnectionRequest.mockClear()
    act(() => { pending('/c', c.id); connect('/c') })
    expect(useThreadStore.getState().threadListProjectKey).toBe('/a')
    expect(sendConnectionRequest).not.toHaveBeenCalled()
    await act(async () => { view.rerender(<View path="/c" />) })
    expect(sendConnectionRequest.mock.calls.some(([key, method, params]) =>
      key === '/c' && method === 'thread/resume' && params.threadId === c.id)).toBe(true)
  })

  it('restores the selected thread when the current connection is replaced without navigation', async () => {
    const { view } = await warmB()
    sendConnectionRequest.mockClear()
    await act(async () => { connect('/a', 'replacement'); view.rerender(<View path="/a" />) })
    expect(useThreadStore.getState().activeThreadId).toBe(a.id)
    expect(sendConnectionRequest.mock.calls.some(([key, method]) => key === '/a' && method === 'thread/resume')).toBe(true)
    expect(screen.getByRole('log')).toHaveTextContent('Answer thread-a')
  })

  it.each(['expired', 'reconnected'] as const)('restores a warm view whose attachment is %s', async reason => {
    const { view } = await warmB()
    if (reason === 'expired') {
      vi.useFakeTimers({ toFake: ['Date'] })
      vi.setSystemTime(Date.now() + 3 * 60 * 60 * 1000 + 1)
    }
    sendConnectionRequest.mockClear()
    await act(async () => {
      pending()
      connect('/b', reason === 'reconnected' ? 'replacement' : '/b-1')
      view.rerender(<View path="/b" />)
    })
    expect(sendConnectionRequest.mock.calls.some(([, method]) => method === 'thread/resume')).toBe(true)
    expect(screen.getByRole('log')).toHaveTextContent('Answer thread-b')
  })
})

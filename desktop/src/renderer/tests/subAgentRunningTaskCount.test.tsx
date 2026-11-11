import { act, cleanup, renderHook } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { RawNotificationPayload } from '../../shared/appServerBoundary'
import { disposeSummaryFeeds, useRunningTaskCount } from '../components/conversation/summary/store'
import type { SummaryServices } from '../components/conversation/summary/summaryServices'
import { listDesktopPluginSubAgents, onDesktopPluginSubAgentsChange } from '../plugins/desktopPluginSubAgents'
import { useConnectionStore } from '../stores/connectionStore'
import { useSubAgentStore } from '../stores/subAgentStore'
import { syncSubAgentTaskActivity } from '../stores/subAgentTaskActivity'
import { registerThreadConnection, releaseThreadConnection } from '../stores/threadConnectionStore'
import { useThreadStore } from '../stores/threadStore'
import { deferred } from './subAgentFixtures'

const parent = 'parent-B'
const child = 'child-B'
const source = {
  threadId: parent, connectionKey: 'workspace', connectionId: 'connection', workspaceKey: 'workspace',
  capabilities: { subAgentSessions: true }
}
const listeners = new Set<(payload: RawNotificationPayload) => void>()
const terminal = { sessionId: 'term-owned', threadId: child, turnId: 'turn-1', callId: 'call-1', command: 'work', status: 'running' }
let terminals: typeof terminal[]
const request = vi.fn()
const children = () => useSubAgentStore.getState().childrenByParent.get(parent) ?? []

function notify(method: string, params: unknown): void {
  for (const listener of listeners) listener({ ...source, method, params })
}

const services = {
  subagents: { list: listDesktopPluginSubAgents, onChange: onDesktopPluginSubAgentsChange },
  appServer: {
    connectionKey: () => 'workspace',
    onConnectionChange: () => () => {},
    request: (method: string, params: unknown) => request(source.connectionKey, method, params),
    onNotification(method: string, callback: (params: unknown) => void) {
      const listener = (payload: RawNotificationPayload): void => {
        if (payload.method === method) callback(payload.params)
      }
      listeners.add(listener)
      return () => listeners.delete(listener)
    }
  }
} as unknown as SummaryServices

beforeEach(() => {
  useSubAgentStore.getState().reset()
  useThreadStore.getState().reset()
  listeners.clear()
  request.mockReset()
  terminals = [terminal]
  useConnectionStore.setState({ status: 'connected', ...source })
  registerThreadConnection(parent, source)
  vi.stubGlobal('window', { api: { appServer: {
    sendConnectionRequest: request,
    onNotificationRaw(listener: (payload: RawNotificationPayload) => void) {
      listeners.add(listener)
      return () => listeners.delete(listener)
    }
  } } })
  request.mockImplementation(async (_connection, method, params) => {
    if (method === 'subagent/children/list') return { data: [{
      edge: { parentThreadId: parent, childThreadId: child, status: 'open' },
      thread: { id: child, runtime: { running: false } }
    }] }
    if (method === 'terminal/list') return { terminals: params.threadId === child ? terminals : [] }
    if (method === 'workflow/run/list') return { runs: [] }
    return { data: [] }
  })
})

afterEach(() => {
  cleanup()
  disposeSummaryFeeds()
  useSubAgentStore.getState().reset()
  useThreadStore.getState().reset()
  releaseThreadConnection(parent)
  useConnectionStore.getState().reset()
  vi.unstubAllGlobals()
})

async function discover(): Promise<void> {
  await useSubAgentStore.getState().ensureChildren(parent)
  await syncSubAgentTaskActivity(parent, children())
}

describe('subagent activity in the running task count', () => {
  it('settles live completions and reconciles missed completions on idle without leaving the conversation', async () => {
    const count = renderHook(() => useRunningTaskCount(services, parent))
    await act(discover)
    expect(count.result.current).toBe(1)
    expect(listDesktopPluginSubAgents(parent)[0].state).toBe('working')

    act(() => { notify('terminal/completed', { terminal: { ...terminal, status: 'completed' } }) })
    expect(listDesktopPluginSubAgents(parent)[0].state).toBe('done')
    expect(count.result.current).toBe(0)

    const next = { ...terminal, sessionId: 'term-next', callId: 'call-2' }
    terminals = [next]
    act(() => { notify('terminal/started', { terminal: next }) })
    expect(count.result.current).toBe(1)

    const refreshed = deferred<void>()
    terminals = []
    request.mockImplementation(async (_connection, method, params) => {
      if (method === 'terminal/list') {
        if (params.threadId === child) refreshed.resolve()
        return { terminals: [] }
      }
      return { runs: [] }
    })
    await act(async () => {
      notify('thread/status/changed', { threadId: child, runtime: { running: false } })
      await refreshed.promise
    })
    expect(listDesktopPluginSubAgents(parent)[0].state).toBe('done')
    expect(count.result.current).toBe(0)
  })

  it('reconciles tasks on reopening a cached parent without reloading child identities', async () => {
    const first = renderHook(() => useRunningTaskCount(services, parent))
    await act(discover)
    expect(first.result.current).toBe(1)
    first.unmount()
    const childReads = request.mock.calls.filter(([, method]) => method === 'subagent/children/list').length
    terminals = []

    const reopened = renderHook(() => useRunningTaskCount(services, parent))
    await act(async () => { await useSubAgentStore.getState().ensureChildren(parent) })
    expect(reopened.result.current).toBe(0)
    expect(listDesktopPluginSubAgents(parent)[0].state).toBe('done')
    expect(request.mock.calls.filter(([, method]) => method === 'subagent/children/list')).toHaveLength(childReads)
  })
})

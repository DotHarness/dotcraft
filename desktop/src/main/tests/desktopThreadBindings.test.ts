import { describe, expect, it, vi } from 'vitest'
import {
  hasDesktopThreadTools, resetDesktopThreadToolBindings, sendDesktopAppServerRequest, threadTaskConnection,
  type AppServerRequestClient
} from '../desktopRuntimeThreadTools'
import { CREATOR_CONTEXT_KEY, setCreatorThread } from '../creatorMode'
import { registerClientAdditionalMcpProvider, unregisterClientAdditionalMcpProvider } from '../repl/nodeReplMcpBindings'
import { DESKTOP_LINKED_CHATS_CONTEXT_KEY, setDesktopLinkedChatsSource } from '../threadTasks/linkedChatsContext'
import type { LinkedChat } from '../threadTasks/linkedChatStore'

function client(handler?: (method: string) => Promise<unknown>): AppServerRequestClient {
  return { sendRequest: vi.fn(handler ?? (async (method) => method === 'thread/start' || method === 'thread/resume'
    ? { thread: { id: 'same-thread' } } : {})) as AppServerRequestClient['sendRequest'] }
}

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>((res) => { resolve = res })
  return { promise, resolve }
}

const rebind = { supportsDynamicToolRebind: true }

describe('Desktop connection-local thread bindings', () => {
  it('re-declares a thread with its Creator context before the turn after Creator mode changes', async () => {
    const connection = client()
    const resumedContexts = () => vi.mocked(connection.sendRequest).mock.calls
      .filter(([method]) => method === 'thread/resume')
      .map(([, params]) => Object.keys((params as { additionalContext: Record<string, unknown> }).additionalContext))
    await sendDesktopAppServerRequest(connection, 'thread/start', {})
    await sendDesktopAppServerRequest(connection, 'turn/start', { threadId: 'same-thread' }, undefined, rebind)
    expect(resumedContexts()).toEqual([])

    setCreatorThread('same-thread', true)
    await sendDesktopAppServerRequest(connection, 'turn/start', { threadId: 'same-thread' }, undefined, rebind)
    await sendDesktopAppServerRequest(connection, 'turn/start', { threadId: 'same-thread' }, undefined, rebind)
    expect(resumedContexts()).toHaveLength(1)
    expect(resumedContexts()[0]).toContain(CREATOR_CONTEXT_KEY)

    setCreatorThread('same-thread', false)
    await sendDesktopAppServerRequest(connection, 'turn/start', { threadId: 'same-thread' }, undefined, rebind)
    expect(resumedContexts()).toHaveLength(2)
    expect(resumedContexts()[1]).not.toContain(CREATOR_CONTEXT_KEY)
  })

  it('retains both clients bindings and resets only the selected client', async () => {
    const first = client()
    const second = client()
    await Promise.all([
      sendDesktopAppServerRequest(first, 'thread/start', {}),
      sendDesktopAppServerRequest(second, 'thread/start', {})
    ])
    expect(hasDesktopThreadTools(first, 'same-thread', {})).toBe(true)
    expect(hasDesktopThreadTools(second, 'same-thread', {})).toBe(true)
    resetDesktopThreadToolBindings(first)
    expect(hasDesktopThreadTools(first, 'same-thread', {})).toBe(false)
    expect(hasDesktopThreadTools(second, 'same-thread', {})).toBe(true)
    await sendDesktopAppServerRequest(second, 'turn/start', { threadId: 'same-thread' }, undefined, rebind)
    expect(vi.mocked(second.sendRequest).mock.calls.map(([method]) => method)).toEqual(['thread/start', 'turn/start'])
  })

  it('rejects a late start from an invalidated generation without changing fresh bindings', async () => {
    const pending = deferred<unknown>()
    let starts = 0
    const connection = client(async () => ++starts === 1 ? pending.promise : { thread: { id: 'new-thread' } })
    const old = sendDesktopAppServerRequest(connection, 'thread/start', {})
    const rejected = expect(old).rejects.toThrow('no longer active')
    resetDesktopThreadToolBindings(connection)
    await sendDesktopAppServerRequest(connection, 'thread/start', {})
    pending.resolve({ thread: { id: 'old-thread' } })
    await rejected
    expect(hasDesktopThreadTools(connection, 'old-thread', {})).toBe(false)
    expect(hasDesktopThreadTools(connection, 'new-thread', {})).toBe(true)
  })

  it('does not share pending resumes between clients or send an old visualization after reset', async () => {
    const pending = deferred<unknown>()
    const resumed = deferred<void>()
    const first = client(async (method) => {
      if (method === 'thread/resume') { resumed.resolve(); return pending.promise }
      return {}
    })
    const second = client()
    const params = { threadId: 'same-thread', path: 'chart.html' }
    const old = sendDesktopAppServerRequest(first, 'visualization/view/open', params, undefined, rebind)
    const rejected = expect(old).rejects.toThrow('no longer active')
    await resumed.promise
    await sendDesktopAppServerRequest(second, 'visualization/view/open', params, undefined, rebind)
    resetDesktopThreadToolBindings(first)
    pending.resolve({ thread: { id: 'same-thread' } })
    await rejected
    expect(vi.mocked(first.sendRequest).mock.calls.map(([method]) => method)).toEqual(['thread/resume'])
    expect(hasDesktopThreadTools(second, 'same-thread', {})).toBe(true)
    await sendDesktopAppServerRequest(second, 'visualization/view/open', params, undefined, rebind)
    expect(vi.mocked(second.sendRequest).mock.calls.filter(([method]) => method === 'thread/resume')).toHaveLength(1)
  })

  it('coalesces concurrent turn rebinds and carries the explicit window owner into task connections', async () => {
    const pending = deferred<unknown>()
    const resumed = deferred<void>()
    const connection = client(async (method) => {
      if (method === 'thread/resume') { resumed.resolve(); return pending.promise }
      return {}
    })
    const taskConnection = threadTaskConnection(connection, '/workspace', { ...rebind, ownerId: 77 })
    expect(taskConnection.ownerId).toBe(77)
    const first = taskConnection.request('turn/start', { threadId: 'same-thread' })
    const second = taskConnection.request('turn/enqueue', { threadId: 'same-thread' })
    await resumed.promise
    expect(vi.mocked(connection.sendRequest).mock.calls.filter(([method]) => method === 'thread/resume')).toHaveLength(1)
    pending.resolve({ thread: { id: 'same-thread' } })
    await Promise.all([first, second])
    expect(vi.mocked(connection.sendRequest).mock.calls.map(([method]) => method)).toEqual(['thread/resume', 'turn/start', 'turn/enqueue'])
  })

  it('injects and rebinds desktop.linkedChats before turns and turn/notify as linked chat states change', async () => {
    let links: LinkedChat[] = []
    setDesktopLinkedChatsSource({ listForThread: (threadId) => (threadId === 'same-thread' ? links : []) })
    try {
      const connection = client()
      const resumedPayloads = () => vi.mocked(connection.sendRequest).mock.calls
        .filter(([method]) => method === 'thread/resume')
        .map(([, params]) => (params as { additionalContext: Record<string, { value: string }> }).additionalContext)

      await sendDesktopAppServerRequest(connection, 'thread/start', {})
      await sendDesktopAppServerRequest(connection, 'turn/start', { threadId: 'same-thread' }, undefined, rebind)
      expect(resumedPayloads()).toEqual([])

      links = [{
        parent: { threadId: 'same-thread', projectId: 'chats', workspacePath: '/chats' },
        child: { threadId: 'child-1', projectId: 'dotcraft', workspacePath: '/dotcraft', projectName: 'dotcraft', title: 'Fix tests' },
        status: 'running',
        followed: true,
        createdAt: '2026-11-27T01:00:00Z'
      }]
      await sendDesktopAppServerRequest(connection, 'turn/start', { threadId: 'same-thread' }, undefined, rebind)
      expect(resumedPayloads()).toHaveLength(1)
      expect(resumedPayloads()[0]?.[DESKTOP_LINKED_CHATS_CONTEXT_KEY]?.value).toContain('"Fix tests" (id: child-1, project: dotcraft [dotcraft]): working, followed')

      await sendDesktopAppServerRequest(connection, 'turn/start', { threadId: 'same-thread' }, undefined, rebind)
      expect(resumedPayloads()).toHaveLength(1)

      links = [{ ...links[0]!, status: 'waitingForInput', waitingFor: 'approval' }]
      await sendDesktopAppServerRequest(connection, 'turn/notify', { threadId: 'same-thread' }, undefined, rebind)
      expect(resumedPayloads()).toHaveLength(2)
      expect(resumedPayloads()[1]?.[DESKTOP_LINKED_CHATS_CONTEXT_KEY]?.value).toContain('needs the user (approval)')

      links = []
      await sendDesktopAppServerRequest(connection, 'turn/start', { threadId: 'same-thread' }, undefined, rebind)
      expect(resumedPayloads()).toHaveLength(3)
      expect(resumedPayloads()[2]).not.toHaveProperty(DESKTOP_LINKED_CHATS_CONTEXT_KEY)
    } finally {
      setDesktopLinkedChatsSource(null)
    }
  })

  it('rebinds on the next turn when MCP revision changes while thread/start is in flight', async () => {
    const startResponse = deferred<unknown>()
    const connection = client(async (method) => {
      if (method === 'thread/start') return startResponse.promise
      return { thread: { id: 'same-thread' } }
    })
    let enabled = false
    registerClientAdditionalMcpProvider(connection, {
      prepare: () => undefined,
      getServers: () => (enabled ? [{ name: 'node_repl', enabled: true, transport: 'streamableHttp', url: 'http://127.0.0.1:9999/mcp/r' }] : []),
      getRevision: () => (enabled ? '1:r:browser' : '0::')
    })
    try {
      const starting = sendDesktopAppServerRequest(connection, 'thread/start', {})
      enabled = true
      startResponse.resolve({ thread: { id: 'same-thread' } })
      await starting

      await sendDesktopAppServerRequest(connection, 'turn/start', { threadId: 'same-thread' }, undefined, rebind)
      const resumes = vi.mocked(connection.sendRequest).mock.calls.filter(([method]) => method === 'thread/resume')
      expect(resumes).toHaveLength(1)
      expect((resumes[0]?.[1] as { additionalMcpServers?: Array<{ name: string }> }).additionalMcpServers)
        .toEqual([expect.objectContaining({ name: 'node_repl' })])
    } finally {
      unregisterClientAdditionalMcpProvider(connection)
    }
  })
})

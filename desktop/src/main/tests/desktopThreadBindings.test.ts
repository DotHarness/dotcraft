import { describe, expect, it, vi } from 'vitest'
import {
  hasDesktopThreadTools, resetDesktopThreadToolBindings, sendDesktopAppServerRequest, threadTaskConnection,
  type AppServerRequestClient
} from '../desktopRuntimeThreadTools'

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
    const params = { threadId: 'same-thread', path: '/chart.html' }
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
})

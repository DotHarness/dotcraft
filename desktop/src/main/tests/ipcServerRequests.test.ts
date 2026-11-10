import type { BrowserWindow } from 'electron'
import { describe, expect, it, vi } from 'vitest'
import {
  broadcastServerRequest, createServerRequestBridge,
  resolveServerRequestBridge
} from '../ipcServerRequests'

function owner(): BrowserWindow {
  return { webContents: { id: 1, send: vi.fn() }, isDestroyed: () => false } as unknown as BrowserWindow
}

function input(win: BrowserWindow, connectionId: string, requestId: number, signal?: AbortSignal) {
  const bridge = createServerRequestBridge(undefined, win,
    { connectionKey: 'workspace', connectionId, wireRequestId: requestId }, signal)
  broadcastServerRequest(win, {
    bridgeId: bridge.bridgeId, wireRequestId: requestId, connectionId,
    method: 'mcpServer/elicitation/request', params: { threadId: `thread-${requestId}` }
  })
  return bridge
}

describe('server request lifecycle', () => {
  it('settles concurrent inputs in reverse order and ignores duplicate answers', async () => {
    const win = owner()
    const first = input(win, 'one', 10)
    const second = input(win, 'one', 20)
    resolveServerRequestBridge(second.bridgeId, { project: 'B' }, win.webContents.id)
    await expect(second.promise).resolves.toEqual({ project: 'B' })
    resolveServerRequestBridge(first.bridgeId, { project: 'A' }, win.webContents.id)
    await expect(first.promise).resolves.toEqual({ project: 'A' })
    const calls = vi.mocked(win.webContents.send).mock.calls.length
    resolveServerRequestBridge(second.bridgeId, { project: 'wrong' }, win.webContents.id)
    expect(win.webContents.send).toHaveBeenCalledTimes(calls)
    expect(win.webContents.send).toHaveBeenCalledWith('appserver:server-request-settled', {
      bridgeId: first.bridgeId, connectionId: 'one', wireRequestId: 10
    })
  })

  it('withdraws only the signalled input even when connections reuse request IDs', async () => {
    const win = owner()
    const cancel = new AbortController()
    const first = input(win, 'one', 1, cancel.signal)
    const second = input(win, 'two', 1)
    const rejected = expect(first.promise).rejects.toThrow('cancelled')
    cancel.abort()
    await rejected
    expect(win.webContents.send).toHaveBeenCalledWith('appserver:server-request-settled', {
      bridgeId: first.bridgeId, connectionId: 'one', wireRequestId: 1
    })
    resolveServerRequestBridge(first.bridgeId, 'late', win.webContents.id)
    resolveServerRequestBridge(second.bridgeId, 'current', win.webContents.id)
    await expect(second.promise).resolves.toBe('current')
  })
})

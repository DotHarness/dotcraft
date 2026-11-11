import { EventEmitter } from 'node:events'
import { afterEach, describe, expect, it, vi } from 'vitest'

const electron = vi.hoisted(() => ({
  handlers: new Map<string, (...args: unknown[]) => unknown>(),
  send: vi.fn()
}))

vi.mock('electron', () => ({
  BrowserWindow: {
    getAllWindows: () => [{ isDestroyed: () => false, webContents: { send: electron.send } }]
  },
  ipcMain: {
    handle: (channel: string, handler: (...args: unknown[]) => unknown) => electron.handlers.set(channel, handler),
    removeHandler: (channel: string) => electron.handlers.delete(channel),
    on: vi.fn(),
    removeListener: vi.fn(),
    removeAllListeners: vi.fn()
  }
}))

import { createWindowIpcMain } from '../windowIpc'
import { registerOratorioIpc, validateRequest } from './oratorioIpc'

const releases: Array<() => void> = []
afterEach(() => { releases.splice(0).forEach((release) => release()); vi.unstubAllGlobals() })

function windowFixture(id: number) {
  const webContents = Object.assign(new EventEmitter(), { id, isDestroyed: () => false, send: vi.fn() })
  const window = Object.assign(new EventEmitter(), { webContents, isDestroyed: () => false })
  const ipc = createWindowIpcMain(window as never)
  releases.push(() => ipc.dispose())
  return { webContents, ipc, invoke: (channel: string, ...args: unknown[]) => electron.handlers.get(channel)?.({ sender: webContents }, ...args) }
}

describe('Oratorio IPC validation', () => {
  it('accepts only bounded server routes', () => {
    expect(validateRequest({ method: 'GET', path: '/api/v1/tasks' })).toEqual({
      method: 'GET',
      path: '/api/v1/tasks'
    })
    expect(validateRequest({ method: 'POST', path: '/api/v1/local-tasks', body: { title: 'Task' } })).toEqual({
      method: 'POST',
      path: '/api/v1/local-tasks',
      body: { title: 'Task' }
    })
    expect(validateRequest({ method: 'PATCH', path: '/api/v1/follow-up-drafts/draft-1', body: { body: 'Updated' } })).toEqual({
      method: 'PATCH', path: '/api/v1/follow-up-drafts/draft-1', body: { body: 'Updated' }
    })
    expect(() => validateRequest({ method: 'PATCH', path: '/api/v1/review-drafts/draft-1', body: { summaryBody: 'Updated' } })).toThrow('oratorio.invalid_path')
    expect(validateRequest({ method: 'POST', path: '/api/v1/review-drafts/draft-1/comments/comment-1/resolve', body: { resolutionKind: 'fixed' } })).toBeTruthy()
    expect(validateRequest({ method: 'GET', path: '/api/v1/sources' })).toBeTruthy()
    expect(validateRequest({ method: 'POST', path: '/api/v1/sources/sync-jobs', body: { provider: 'github', mode: 'full', projects: ['example-owner/project'] } })).toBeTruthy()
    expect(validateRequest({ method: 'GET', path: '/api/v1/sources/sync-schedules' })).toBeTruthy()
    expect(validateRequest({ method: 'GET', path: '/api/v1/sources/sync-jobs/job-1?provider=gitlab' })).toBeTruthy()
    expect(validateRequest({ method: 'PUT', path: '/api/v1/sources/github/sync-schedule', body: { enabled: true, intervalSeconds: 900 } })).toBeTruthy()
  })

  it('rejects unbounded methods, paths, and bodies', () => {
    const invalid = [
      null,
      { method: 'DELETE', path: '/v1/board' },
      { method: 'GET', path: 'http://example.test/token' },
      { method: 'POST', path: '/api/v1/tasks' },
      { method: 'GET', path: '/api/v1/settings/diagnostics' },
      { method: 'GET', path: '/api/v1/dotcraft/status' },
      { method: 'GET', path: '/api/v1/runs/run-1' },
      { method: 'GET', path: '/api/v1/sources/sync-jobs/job-1' },
      { method: 'GET', path: '/api/v1/sources/sync-jobs/active?provider=github' },
      { method: 'POST', path: '/api/v1/sources/unknown/sync-jobs', body: {} },
      { method: 'GET', path: '/api/v1/tasks', body: [] }
    ]
    for (const request of invalid) {
      expect(() => validateRequest(request), JSON.stringify(request)).toThrow(/^oratorio\./)
    }
  })
})

describe('Oratorio local context', () => {
  it('keeps handoff requests and approvals with the initiating sender', async () => {
    const first = windowFixture(301)
    const second = windowFixture(302)
    const hub = {
      ensureManagedService: vi.fn().mockResolvedValue({ state: 'running', endpoint: 'http://127.0.0.1:9001', accessToken: 'service-secret' }),
      ensureAppServer: vi.fn(async (workspacePath: string) => ({ workspacePath, endpoints: { appServerWebSocket: 'ws://127.0.0.1:9010' }, serviceStatus: {} }))
    }
    const local = registerOratorioIpc(() => 'F:/a', () => hub as never, undefined, first.ipc, first.webContents as never)
    const other = registerOratorioIpc(() => 'F:/b', () => hub as never, undefined, second.ipc, second.webContents as never)
    releases.push(local.dispose, other.dispose)
    vi.stubGlobal('fetch', vi.fn(async () => new Response(JSON.stringify({ connection: { accountLabel: 'first owner' } }))))
    const { openDesktopServiceHandoff } = await import('../desktopServiceHandoff')
    await openDesktopServiceHandoff('dotcraft-service://oratorio/connect?app=com.dotharness.oratorio&request=one&token=secret&workspace=F%3A%2Fa', 301)
    const handoff = first.invoke('oratorio:get-pending-handoff') as { requestId: string }
    expect(handoff).toMatchObject({ workspacePath: 'F:/a' })
    expect(second.invoke('oratorio:get-pending-handoff')).toBeNull()
    await expect(second.invoke('oratorio:resolve-handoff', { requestId: handoff.requestId, approved: true })).rejects.toMatchObject({ code: 'oratorio.invalidHandoffRequest' })
    expect(second.webContents.send).not.toHaveBeenCalled()
    await first.invoke('oratorio:resolve-handoff', { requestId: handoff.requestId, approved: false })
    local.dispose()
    await expect(openDesktopServiceHandoff('dotcraft-service://oratorio/connect', 301)).rejects.toThrow()
    await expect(second.invoke('oratorio:get-context')).resolves.toMatchObject({ workspacePath: 'F:/b' })
  })

  it('routes simultaneous local and remote window requests and keeps the other window alive on close', async () => {
    const first = windowFixture(101)
    const second = windowFixture(202)
    const hub = { ensureManagedService: vi.fn().mockResolvedValue({
      state: 'running', endpoint: 'http://127.0.0.1:9001', accessToken: 'local-secret'
    }) }
    const local = registerOratorioIpc(() => 'F:/local', () => hub as never, undefined, first.ipc, first.webContents as never)
    const remote = registerOratorioIpc(() => 'F:/ssh-owner', () => hub as never, async () => ({
      endpoint: 'http://127.0.0.1:9002', accessToken: 'remote-secret', workspacePath: '/remote',
      appServerEndpoint: 'ws://127.0.0.1:9010', appServerIdentity: 'remote:id'
    }), second.ipc, second.webContents as never)
    releases.push(local.dispose, remote.dispose)
    const fetcher = vi.fn(async (_url: string, options: RequestInit) => new Response(JSON.stringify({ auth: (options.headers as Record<string, string>).Authorization })))
    vi.stubGlobal('fetch', fetcher)

    const contexts = await Promise.all([first.invoke('oratorio:get-context'), second.invoke('oratorio:get-context')])
    expect(contexts).toMatchObject([{ provider: 'local', workspacePath: 'F:/local' }, { provider: 'remote', workspacePath: '/remote' }])
    const results = await Promise.all([first.invoke('oratorio:request', { method: 'GET', path: '/api/v1/tasks' }), second.invoke('oratorio:request', { method: 'GET', path: '/api/v1/tasks' })])
    expect(results).toMatchObject([{ data: { auth: 'Bearer local-secret' } }, { data: { auth: 'Bearer remote-secret' } }])
    local.contextChanged()
    expect(first.webContents.send).toHaveBeenCalledWith('oratorio:event', expect.objectContaining({ type: 'context-changed' }))
    expect(second.webContents.send).not.toHaveBeenCalled()

    local.dispose()
    first.ipc.dispose()
    expect(() => first.invoke('oratorio:get-context')).toThrow()
    await expect(second.invoke('oratorio:get-context')).resolves.toMatchObject({ provider: 'remote', workspacePath: '/remote' })
  })

  it('reads the current workspace lazily and notifies subscribers on switches', async () => {
    let workspace = 'F:/workspace/one'
    const hub = {
      ensureManagedService: vi.fn().mockResolvedValue({
        serviceId: 'oratorio', state: 'running', pid: 42,
        endpoint: 'http://127.0.0.1:9999', accessToken: 'secret'
      })
    }
    const window = windowFixture(1)
    const handle = registerOratorioIpc(() => workspace, () => hub as never, undefined, window.ipc, window.webContents as never)
    releases.push(handle.dispose)
    await expect(window.invoke('oratorio:get-context')).resolves.toMatchObject({ workspacePath: 'F:/workspace/one', provider: 'local' })

    workspace = 'F:/workspace/two'
    handle.contextChanged()

    await expect(window.invoke('oratorio:get-context')).resolves.toMatchObject({ workspacePath: 'F:/workspace/two' })
    expect(window.webContents.send).toHaveBeenLastCalledWith('oratorio:event', expect.objectContaining({
      type: 'context-changed'
    }))
  })
})

import { describe, expect, it, vi } from 'vitest'

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
    removeAllListeners: vi.fn()
  }
}))

import { notifyOratorioContextChanged, registerOratorioIpc, validateRequest } from './oratorioIpc'

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
    expect(validateRequest({ method: 'PATCH', path: '/api/v1/review-drafts/draft-1', body: { summaryBody: 'Updated' } })).toEqual({
      method: 'PATCH', path: '/api/v1/review-drafts/draft-1', body: { summaryBody: 'Updated' }
    })
    expect(validateRequest({ method: 'POST', path: '/api/v1/review-drafts/draft-1/comments/comment-1/resolve', body: { resolutionKind: 'fixed' } })).toBeTruthy()
    expect(validateRequest({ method: 'GET', path: '/api/v1/sources/sync-schedules' })).toBeTruthy()
    expect(validateRequest({ method: 'GET', path: '/api/v1/sources/sync-jobs/job-1?provider=gitlab' })).toBeTruthy()
    expect(validateRequest({ method: 'PUT', path: '/api/v1/sources/github/sync-schedule', body: { enabled: true, intervalSeconds: 900 } })).toBeTruthy()
  })

  it.each([
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
  ])('rejects invalid request %j', (request) => {
    expect(() => validateRequest(request)).toThrow(/^oratorio\./)
  })
})

describe('Oratorio local context', () => {
  it('reads the current workspace lazily and notifies subscribers on switches', async () => {
    let workspace = 'F:/workspace/one'
    const hub = {
      ensureManagedService: vi.fn().mockResolvedValue({
        serviceId: 'oratorio', state: 'running', pid: 42,
        endpoint: 'http://127.0.0.1:9999', accessToken: 'secret'
      })
    }
    registerOratorioIpc(() => workspace, () => hub as never, () => 'oratorio.exe')
    const getContext = electron.handlers.get('oratorio:get-context')
    expect(getContext).toBeDefined()
    await expect(getContext?.()).resolves.toMatchObject({ workspacePath: 'F:/workspace/one', provider: 'local' })

    workspace = 'F:/workspace/two'
    notifyOratorioContextChanged()

    await expect(getContext?.()).resolves.toMatchObject({ workspacePath: 'F:/workspace/two' })
    expect(electron.send).toHaveBeenLastCalledWith('oratorio:event', expect.objectContaining({
      type: 'context-changed'
    }))
  })
})

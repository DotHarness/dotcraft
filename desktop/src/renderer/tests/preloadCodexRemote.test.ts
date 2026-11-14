import { beforeAll, expect, it, vi } from 'vitest'
import type { CodexRemoteApi } from '../../shared/codexRemote'

const electron = vi.hoisted(() => ({
  api: null as unknown,
  listeners: new Map<string, Set<(event: unknown, payload: unknown) => void>>(),
  send: vi.fn()
}))
vi.mock('electron', () => ({
  contextBridge: { exposeInMainWorld: (_key: string, api: unknown) => { electron.api = api } },
  ipcRenderer: {
    on: (channel: string, listener: (event: unknown, payload: unknown) => void) => {
      const listeners = electron.listeners.get(channel) ?? new Set()
      listeners.add(listener)
      electron.listeners.set(channel, listeners)
    },
    removeListener: (channel: string, listener: (event: unknown, payload: unknown) => void) =>
      electron.listeners.get(channel)?.delete(listener),
    send: electron.send,
    invoke: vi.fn()
  },
  shell: { openPath: async () => '' },
  webFrame: { setZoomFactor: () => {} },
  webUtils: { getPathForFile: () => '' }
}))
beforeAll(async () => { await import('../../preload/index') })

it('keeps the main subscription until the last renderer listener is removed', () => {
  const api = (electron.api as { codexRemote: CodexRemoteApi }).codexRemote
  const first = vi.fn()
  const second = vi.fn()
  const stopFirst = api.onEvent(first)
  const stopSecond = api.onEvent(second)
  expect(electron.send.mock.calls.filter(([channel]) => channel === 'codexRemote:subscribe')).toHaveLength(1)
  stopFirst()
  stopFirst()
  for (const listener of electron.listeners.get('codexRemote:event')!) listener({}, { kind: 'paired' })
  expect(first).not.toHaveBeenCalled()
  expect(second).toHaveBeenCalledWith({ kind: 'paired' })
  expect(electron.send).not.toHaveBeenCalledWith('codexRemote:unsubscribe')
  stopSecond()
  expect(electron.send).toHaveBeenCalledWith('codexRemote:unsubscribe')
  expect(electron.listeners.get('codexRemote:event')?.size).toBe(0)
})

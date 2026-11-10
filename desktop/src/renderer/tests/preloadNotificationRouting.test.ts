import { beforeAll, describe, expect, it, vi } from 'vitest'
import type { RawNotificationPayload, RawServerRequestPayload, ServerRequestSettledPayload } from '../../shared/appServerBoundary'

const electronMock = vi.hoisted(() => ({
  ipcHandlers: new Map<string, (event: unknown, ...args: unknown[]) => void>(),
  state: { exposedApi: null as unknown }
}))

vi.mock('electron', () => ({
  contextBridge: {
    exposeInMainWorld: (_key: string, value: unknown): void => {
      electronMock.state.exposedApi = value
    }
  },
  ipcRenderer: {
    on: (channel: string, handler: (event: unknown, ...args: unknown[]) => void): void => {
      electronMock.ipcHandlers.set(channel, handler)
    },
    removeListener: (): void => {},
    send: (): void => {},
    invoke: async (): Promise<unknown> => undefined
  },
  shell: { openPath: async (): Promise<string> => '' },
  webFrame: { setZoomFactor: (): void => {} },
  webUtils: { getPathForFile: (): string => '' }
}))

interface ExposedApi {
  appServer: {
    onNotification(callback: (payload: RawNotificationPayload) => void): () => void
    onNotificationRaw(callback: (payload: RawNotificationPayload) => void): () => void
    onServerRequest(callback: (payload: RawServerRequestPayload) => void): () => void
    onServerRequestRaw(callback: (payload: RawServerRequestPayload) => void): () => void
    onServerRequestSettled(callback: (payload: ServerRequestSettledPayload) => void): () => void
  }
  window: { onMaximizedChange(callback: (maximized: boolean) => void): () => void }
}

const KNOWN_METHOD = 'item/usage/delta'
const UNKNOWN_METHOD = 'plugin/not-in-contracts'

describe('preload bridge', () => {
  let api: ExposedApi

  beforeAll(async () => {
    await import('../../preload/index')
    api = electronMock.state.exposedApi as ExposedApi
  })

  function emit(channel: string, payload: unknown): void {
    const handler = electronMock.ipcHandlers.get(channel)
    if (!handler) throw new Error(`preload did not subscribe to ${channel}`)
    handler({}, payload)
  }

  it('routes known notifications to typed and raw subscribers, unknown ones to raw only', () => {
    const typed = vi.fn()
    const raw = vi.fn()
    const stopTyped = api.appServer.onNotification(typed)
    const stopRaw = api.appServer.onNotificationRaw(raw)

    const known = { method: KNOWN_METHOD, params: { turnInputTokens: 150 } }
    emit('appserver:delivery', { kind: 'notification', payload: known })
    emit('appserver:delivery', { kind: 'notification', payload: { method: UNKNOWN_METHOD, params: { any: true } } })

    expect(typed).toHaveBeenCalledTimes(1)
    expect(typed).toHaveBeenCalledWith(known)
    expect(raw).toHaveBeenCalledTimes(2)

    stopRaw()
    emit('appserver:delivery', { kind: 'notification', payload: known })
    expect(raw).toHaveBeenCalledTimes(2)
    expect(typed).toHaveBeenCalledTimes(2)
    stopTyped()
  })

  it('preserves wire request identity and routes settled bridges independently', () => {
    const known = vi.fn()
    const raw = vi.fn()
    const settled = vi.fn()
    const stopKnown = api.appServer.onServerRequest(known)
    const stopRaw = api.appServer.onServerRequestRaw(raw)
    const stopSettled = api.appServer.onServerRequestSettled(settled)
    const request = { bridgeId: 'bridge-A', wireRequestId: 42, connectionId: 'original',
      method: 'mcpServer/elicitation/request', params: { threadId: 'A' } }
    emit('appserver:server-request', request)
    expect(known).toHaveBeenCalledWith(request)
    expect(raw).not.toHaveBeenCalled()
    const completion = { bridgeId: 'bridge-A', wireRequestId: 42, connectionId: 'original' }
    emit('appserver:server-request-settled', completion)
    expect(settled).toHaveBeenCalledWith(completion)
    stopSettled()
    stopKnown()
    stopRaw()
  })

  it('updates window maximize subscribers independently', () => {
    const frame = vi.fn()
    const titleBar = vi.fn()
    const stopFrame = api.window.onMaximizedChange(frame)
    const stopTitleBar = api.window.onMaximizedChange(titleBar)

    emit('window:maximized-change', true)
    expect(frame).toHaveBeenCalledWith(true)
    expect(titleBar).toHaveBeenCalledWith(true)

    stopFrame()
    emit('window:maximized-change', false)
    expect(frame).toHaveBeenCalledTimes(1)
    expect(titleBar).toHaveBeenLastCalledWith(false)
    stopTitleBar()
  })
})

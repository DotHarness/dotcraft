import { beforeAll, describe, expect, it, vi } from 'vitest'
import type { RawNotificationPayload } from '../../shared/appServerBoundary'

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
    emit('appserver:notification', known)
    emit('appserver:notification', { method: UNKNOWN_METHOD, params: { any: true } })

    expect(typed).toHaveBeenCalledTimes(1)
    expect(typed).toHaveBeenCalledWith(known)
    expect(raw).toHaveBeenCalledTimes(2)

    stopRaw()
    emit('appserver:notification', known)
    expect(raw).toHaveBeenCalledTimes(2)
    expect(typed).toHaveBeenCalledTimes(2)
    stopTyped()
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

import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { isKnownServerNotification, type RawNotificationPayload } from '../../shared/appServerBoundary'

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

interface PreloadAppServerApi {
  onNotification(callback: (payload: RawNotificationPayload) => void): () => void
  onNotificationRaw(callback: (payload: RawNotificationPayload) => void): () => void
}

const KNOWN_METHOD = 'item/usage/delta'
const UNKNOWN_METHOD = 'plugin/not-in-contracts'

describe('preload appserver notification routing', () => {
  let appServer: PreloadAppServerApi
  let emit: (payload: RawNotificationPayload) => void
  const unsubscribes: Array<() => void> = []

  beforeAll(async () => {
    await import('../../preload/index')
    appServer = (electronMock.state.exposedApi as { appServer: PreloadAppServerApi }).appServer
    const handler = electronMock.ipcHandlers.get('appserver:notification')
    if (!handler) throw new Error('preload did not subscribe to appserver:notification')
    emit = (payload) => handler({}, payload)
  })

  beforeEach(() => {
    while (unsubscribes.length) unsubscribes.pop()?.()
  })

  function subscribeTyped(callback: (payload: RawNotificationPayload) => void): void {
    unsubscribes.push(appServer.onNotification(callback))
  }

  function subscribeRaw(callback: (payload: RawNotificationPayload) => void): void {
    unsubscribes.push(appServer.onNotificationRaw(callback))
  }

  it('treats item/usage/delta as a generated notification', () => {
    expect(isKnownServerNotification({ method: KNOWN_METHOD, params: {} })).toBe(true)
    expect(isKnownServerNotification({ method: UNKNOWN_METHOD, params: {} })).toBe(false)
  })

  it('delivers a known notification to raw and typed subscribers, each exactly once', () => {
    const typed = vi.fn()
    const raw = vi.fn()
    subscribeTyped(typed)
    subscribeRaw(raw)

    const payload = { method: KNOWN_METHOD, params: { turnInputTokens: 150 } }
    emit(payload)

    expect(raw).toHaveBeenCalledTimes(1)
    expect(raw).toHaveBeenCalledWith(payload)
    expect(typed).toHaveBeenCalledTimes(1)
    expect(typed).toHaveBeenCalledWith(payload)
  })

  it('delivers an unknown notification to raw subscribers only', () => {
    const typed = vi.fn()
    const raw = vi.fn()
    subscribeTyped(typed)
    subscribeRaw(raw)

    emit({ method: UNKNOWN_METHOD, params: { any: true } })

    expect(raw).toHaveBeenCalledTimes(1)
    expect(typed).not.toHaveBeenCalled()
  })

  it('gives every raw subscriber one copy of each notification', () => {
    const first = vi.fn()
    const second = vi.fn()
    subscribeRaw(first)
    subscribeRaw(second)

    emit({ method: KNOWN_METHOD, params: {} })
    emit({ method: UNKNOWN_METHOD, params: {} })

    expect(first).toHaveBeenCalledTimes(2)
    expect(second).toHaveBeenCalledTimes(2)
  })

  it('stops delivering to a raw subscriber that unsubscribed', () => {
    const raw = vi.fn()
    const stop = appServer.onNotificationRaw(raw)
    stop()

    emit({ method: KNOWN_METHOD, params: {} })

    expect(raw).not.toHaveBeenCalled()
  })

  it('reaches a plugin-style subscriber that filters by method name', () => {
    const deltas: unknown[] = []
    subscribeRaw((notification) => {
      if (notification.method === KNOWN_METHOD) deltas.push(notification.params)
    })

    emit({ method: 'turn/started', params: {} })
    emit({ method: KNOWN_METHOD, params: { turnOutputTokens: 42 } })

    expect(deltas).toEqual([{ turnOutputTokens: 42 }])
  })
})

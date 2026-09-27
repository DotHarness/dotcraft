import { beforeAll, describe, expect, it, vi } from 'vitest'

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

describe('preload window maximize state', () => {
  let onMaximizedChange: (callback: (maximized: boolean) => void) => () => void
  let emitMaximizedChange: (maximized: boolean) => void

  beforeAll(async () => {
    await import('../../preload/index')
    onMaximizedChange = (electronMock.state.exposedApi as {
      window: { onMaximizedChange: typeof onMaximizedChange }
    }).window.onMaximizedChange
    const handler = electronMock.ipcHandlers.get('window:maximized-change')
    if (!handler) throw new Error('preload did not subscribe to window:maximized-change')
    emitMaximizedChange = (maximized) => handler({}, maximized)
  })

  it('updates both title bar and window frame subscribers independently', () => {
    const frame = vi.fn()
    const titleBar = vi.fn()
    const stopFrame = onMaximizedChange(frame)
    const stopTitleBar = onMaximizedChange(titleBar)

    emitMaximizedChange(true)
    expect(frame).toHaveBeenCalledWith(true)
    expect(titleBar).toHaveBeenCalledWith(true)

    stopFrame()
    emitMaximizedChange(false)
    expect(frame).toHaveBeenCalledTimes(1)
    expect(titleBar).toHaveBeenLastCalledWith(false)

    stopTitleBar()
  })
})

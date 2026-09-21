import { EventEmitter } from 'node:events'
import { afterEach, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({ fromId: vi.fn(), partition: {} }))
vi.mock('electron', () => ({ webContents: { fromId: mocks.fromId }, session: { fromPartition: () => mocks.partition } }))
import { BrowserGuestRegistry } from '../browserGuestRegistry'

function fixture() {
  const owner = Object.assign(new EventEmitter(), { isDestroyed: () => false, send: vi.fn() })
  const win = Object.assign(new EventEmitter(), {
    id: 1,
    isDestroyed: () => false,
    webContents: owner
  }) as unknown as Electron.BrowserWindow
  const page = Object.assign(new EventEmitter(), {
    hostWebContents: owner, session: mocks.partition, isDestroyed: () => false, close: vi.fn()
  })
  mocks.fromId.mockReturnValue(page)
  return { registry: new BrowserGuestRegistry(), win, owner, page }
}

afterEach(() => vi.useRealTimers())

it('registers one guest, keeps it through presentation changes and forwards page clicks', async () => {
  const { registry, win, page, owner } = fixture()
  const ready = registry.request(win, 'tab', 'persist:workspace', { width: 900, height: 600 })
  expect(registry.request(win, 'tab', 'persist:workspace')).toBe(ready)
  expect(registry.list(win)[0]).toMatchObject({ automation: true, visible: false, bounds: { width: 900, height: 600 } })
  registry.bind(win, 'tab', 7)
  await expect(ready).resolves.toBe(page)
  registry.update(win, 'tab', { visible: true })
  registry.update(win, 'tab', { visible: false })
  page.emit('before-mouse-event', {}, { type: 'mouseDown' })
  expect(owner.send).toHaveBeenLastCalledWith('viewer:browser:host-event', { type: 'pointer-down', tabId: 'tab' })
  expect(page.close).not.toHaveBeenCalled()
  registry.clear(win)
  expect(page.close).toHaveBeenCalledOnce()
  expect(registry.list(win)).toEqual([])
})

it('hardens remote guest preferences before attachment', () => {
  const { registry, win, owner } = fixture()
  registry.attachWindow(win)
  const listener = owner.listeners('will-attach-webview')[0] as (
    event: unknown,
    preferences: Electron.WebPreferences
  ) => void
  const preferences = { preload: 'unsafe.js', webSecurity: false, plugins: true }

  listener({}, preferences)

  expect(preferences).toEqual({
    nodeIntegration: false,
    nodeIntegrationInSubFrames: false,
    nodeIntegrationInWorker: false,
    contextIsolation: true,
    sandbox: true,
    webSecurity: true,
    allowRunningInsecureContent: false,
    webviewTag: false,
    plugins: false,
    devTools: true
  })
})

it('rejects pending creation on close and cannot bind a late guest', async () => {
  const { registry, win } = fixture()
  const ready = registry.request(win, 'tab', 'persist:workspace')
  const rejected = expect(ready).rejects.toThrow('Browser page closed')
  registry.remove(win, 'tab')
  await rejected
  expect(() => registry.bind(win, 'tab', 7)).toThrow('does not belong')
})

it('bounds readiness waiting and removes a failed host', async () => {
  vi.useFakeTimers()
  const { registry, win } = fixture()
  const rejected = expect(registry.request(win, 'tab', 'persist:workspace')).rejects.toThrow('did not become ready')
  await vi.advanceTimersByTimeAsync(30_000)
  await rejected
  expect(registry.list(win)).toEqual([])
})

it('does not bind another window’s guest', async () => {
  const { registry, win, page } = fixture()
  const rejected = expect(registry.request(win, 'tab', 'persist:workspace')).rejects.toThrow('closed')
  page.hostWebContents = {} as typeof page.hostWebContents
  expect(() => registry.bind(win, 'tab', 7)).toThrow('does not belong')
  registry.clear(win)
  await rejected
})

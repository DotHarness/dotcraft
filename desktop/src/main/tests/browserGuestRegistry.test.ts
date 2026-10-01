import { EventEmitter } from 'node:events'
import { afterEach, expect, it, vi } from 'vitest'

const mocks = vi.hoisted(() => ({ fromId: vi.fn(), partition: {} }))
vi.mock('electron', () => ({ webContents: { fromId: mocks.fromId }, session: { fromPartition: () => mocks.partition } }))
import { BrowserGuestRegistry } from '../browserGuestRegistry'

function fixture() {
  const owner = Object.assign(new EventEmitter(), {
    isDestroyed: () => false, send: vi.fn(),
    getBackgroundThrottling: () => true, setBackgroundThrottling: vi.fn()
  })
  const win = Object.assign(new EventEmitter(), {
    id: 1,
    isDestroyed: () => false,
    isFocused: () => true,
    isVisible: () => true,
    isMinimized: vi.fn(() => false),
    webContents: owner
  }) as unknown as Electron.BrowserWindow
  const page = Object.assign(new EventEmitter(), {
    setBackgroundThrottling: vi.fn(), hostWebContents: owner, session: mocks.partition, isDestroyed: () => false, close: vi.fn()
  })
  mocks.fromId.mockReturnValue(page)
  return { registry: new BrowserGuestRegistry(), win, owner, page }
}

afterEach(() => vi.useRealTimers())

it('registers one guest, keeps it through presentation changes and forwards page clicks', async () => {
  const { registry, win, page, owner } = fixture()
  const ready = registry.request(win, 'tab', 'persist:workspace', true)
  expect(registry.request(win, 'tab', 'persist:workspace')).toBe(ready)
  expect(registry.list(win)[0]).toMatchObject({ automation: true, visible: false, bounds: { width: 1280, height: 720 } })
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

it('keeps stored bounds and visibility while capturing and restores background throttling', async () => {
  const { registry, win, page } = fixture()
  const ready = registry.request(win, 'tab', 'persist:workspace')
  registry.bind(win, 'tab', 7)
  await ready
  expect(page.setBackgroundThrottling).toHaveBeenLastCalledWith(true)
  const before = registry.list(win)[0]
  registry.setCaptureSurface(win, 'tab', { width: 900, height: 2400 })
  expect(registry.list(win)[0]).toMatchObject({ ...before, captureSurfaceSize: { width: 900, height: 2400 } })
  expect(page.setBackgroundThrottling).toHaveBeenLastCalledWith(false)
  registry.update(win, 'tab', { visible: true })
  registry.setCaptureSurface(win, 'tab', null)
  expect(registry.list(win)[0]).toMatchObject({ bounds: before.bounds, visible: true })
  expect(page.setBackgroundThrottling).toHaveBeenLastCalledWith(false)
  registry.update(win, 'tab', { visible: false, automation: true })
  expect(page.setBackgroundThrottling).toHaveBeenLastCalledWith(false)
  registry.update(win, 'tab', { automation: false })
  expect(page.setBackgroundThrottling).toHaveBeenLastCalledWith(true)
  registry.clear(win)
})

it.each([true, false])('restores owner throttling %s only after the last capture ends', async previous => {
  const { registry, win, page, owner } = fixture()
  owner.getBackgroundThrottling = () => previous
  const a = registry.request(win, 'a', 'persist:workspace')
  registry.bind(win, 'a', 7)
  const other = Object.assign(new EventEmitter(), {
    hostWebContents: owner, session: page.session, isDestroyed: () => false,
    close: vi.fn(), setBackgroundThrottling: vi.fn()
  })
  mocks.fromId.mockReturnValue(other)
  const b = registry.request(win, 'b', 'persist:workspace')
  registry.bind(win, 'b', 8)
  await Promise.all([a, b])
  registry.setCaptureSurface(win, 'a', { width: 800, height: 1800 })
  registry.setCaptureSurface(win, 'b', { width: 800, height: 2400 })
  expect(owner.setBackgroundThrottling).toHaveBeenCalledOnce()
  expect(owner.setBackgroundThrottling).toHaveBeenLastCalledWith(false)
  registry.setCaptureSurface(win, 'a', null)
  expect(owner.setBackgroundThrottling).toHaveBeenCalledOnce()
  registry.remove(win, 'b')
  expect(owner.setBackgroundThrottling).toHaveBeenLastCalledWith(previous)
  registry.clear(win)
})

it('removes guests when the window closes without touching its destroyed web contents', async () => {
  const { registry, win, owner, page } = fixture()
  let destroyed = false
  Object.defineProperties(win, {
    isDestroyed: { value: () => destroyed },
    webContents: { get: () => { if (destroyed) throw new TypeError('Object has been destroyed'); return owner } }
  })
  registry.attachWindow(win)
  const ready = registry.request(win, 'tab', 'persist:workspace')
  registry.bind(win, 'tab', 7)
  await ready
  registry.setCaptureSurface(win, 'tab', { width: 800, height: 1800 })
  destroyed = true
  expect(() => win.emit('closed')).not.toThrow()
  expect(registry.list(win)).toEqual([])
  expect(page.close).toHaveBeenCalledOnce()
})

it('lays a tab out at its explicit viewport, else at its last bounds, else at the default size', async () => {
  const { registry, win } = fixture()
  const closed = expect(registry.request(win, 'tab', 'persist:workspace', true)).rejects.toThrow('closed')
  expect(registry.layoutSize(win, 'tab')).toEqual({ width: 1280, height: 720 })
  registry.update(win, 'tab', { visible: true, bounds: { x: 4, y: 8, width: 700, height: 500 } })
  registry.update(win, 'tab', { visible: false })
  expect(registry.layoutSize(win, 'tab')).toMatchObject({ width: 700, height: 500 })
  registry.update(win, 'tab', { viewport: { width: 390, height: 844 } })
  expect(registry.layoutSize(win, 'tab')).toEqual({ width: 390, height: 844 })
  registry.update(win, 'tab', { viewport: undefined })
  expect(registry.layoutSize(win, 'tab')).toMatchObject({ width: 700, height: 500 })
  registry.remove(win, 'tab')
  await closed
  expect(registry.layoutSize(win, 'tab')).toBeUndefined()
})

it('reports whether a tab is presented', async () => {
  const { registry, win } = fixture()
  const closed = expect(registry.request(win, 'tab', 'persist:workspace')).rejects.toThrow('closed')
  expect(registry.isVisible(win, 'tab')).toBe(false)
  registry.update(win, 'tab', { visible: true })
  expect(registry.isVisible(win, 'tab')).toBe(true)
  vi.mocked(win.isMinimized).mockReturnValue(true)
  expect(registry.isVisible(win, 'tab')).toBe(false)
  vi.mocked(win.isMinimized).mockReturnValue(false)
  registry.update(win, 'tab', { visible: false })
  expect(registry.isVisible(win, 'tab')).toBe(false)
  registry.update(win, 'tab', { visible: true })
  registry.remove(win, 'tab')
  await closed
  expect(registry.isVisible(win, 'tab')).toBe(false)
})

it('publishes cursor moves in the host descriptor', async () => {
  const { registry, win, owner } = fixture()
  const ready = registry.request(win, 'tab', 'persist:workspace')
  registry.bind(win, 'tab', 7)
  await ready
  registry.update(win, 'tab', { visible: true })
  registry.cursors.activate(win, 'tab')
  await registry.cursors.move(win, 'tab', { x: 1, y: 2 }, { waitForArrival: false })
  expect(owner.send).toHaveBeenLastCalledWith('viewer:browser:host-event', {
    type: 'update',
    host: expect.objectContaining({ cursor: expect.objectContaining({ visible: true, x: 1, y: 2, animate: true }) })
  })
})

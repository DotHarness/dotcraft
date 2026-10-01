import { afterEach, expect, it, vi } from 'vitest'
import { startBrowserGuestHost } from '../browser/browserGuestHost'
import type { BrowserHostApi, BrowserHostDescriptor, BrowserHostEvent } from '../../shared/viewer/browserHost'

it('retains guest nodes and navigation while switching visibility and ignores stale bootstrap records', async () => {
  let listener!: (event: BrowserHostEvent) => void
  let initial!: (hosts: BrowserHostDescriptor[]) => void
  const api: BrowserHostApi = {
    list: () => new Promise(resolve => { initial = resolve }),
    bind: vi.fn().mockResolvedValue(undefined), failed: vi.fn().mockResolvedValue(undefined),
    cursorArrived: vi.fn().mockResolvedValue(undefined),
    onEvent: callback => { listener = callback; return vi.fn() }
  }
  const stop = startBrowserGuestHost(api)
  const host: BrowserHostDescriptor = { tabId: 'tab', partition: 'persist:example', automation: true,
    visible: false, bounds: { x: 10, y: 30, width: 800, height: 600 } }
  listener({ type: 'update', host })
  const guest = document.querySelector('webview') as Electron.WebviewTag
  Object.assign(guest, { getWebContentsId: () => 7 })
  guest.dispatchEvent(new Event('dom-ready'))
  expect(api.bind).toHaveBeenCalledWith({ tabId: 'tab', webContentsId: 7 })
  guest.setAttribute('src', 'https://example.com/current')
  listener({ type: 'update', host: { ...host, visible: true } })
  listener({ type: 'update', host })
  expect(document.querySelector('webview')).toBe(guest)
  expect(guest.getAttribute('src')).toBe('https://example.com/current')
  listener({ type: 'update', host: { ...host, captureSurfaceSize: { width: 800, height: 2400 } } })
  expect(document.querySelector('webview')).toBe(guest)
  expect(guest.getAttribute('src')).toBe('https://example.com/current')
  listener({ type: 'update', host })
  expect(document.querySelector('webview')).toBe(guest)
  listener({ type: 'remove', tabId: 'tab' })
  initial([host])
  await Promise.resolve()
  expect(document.querySelector('webview')).toBeNull()
  stop()
})

function mountWithCursor(reduceMotion: 'on' | 'off') {
  document.documentElement.dataset.reduceMotion = reduceMotion
  let listener!: (event: BrowserHostEvent) => void
  const api: BrowserHostApi = {
    list: () => new Promise(() => {}),
    bind: vi.fn().mockResolvedValue(undefined), failed: vi.fn().mockResolvedValue(undefined),
    cursorArrived: vi.fn().mockResolvedValue(undefined),
    onEvent: callback => { listener = callback; return vi.fn() }
  }
  const stop = startBrowserGuestHost(api)
  const host: BrowserHostDescriptor = { tabId: 'tab', partition: 'persist:example', automation: true,
    visible: true, bounds: { x: 0, y: 0, width: 800, height: 600 } }
  const update = (changes: Partial<BrowserHostDescriptor>) => listener({ type: 'update', host: { ...host, ...changes } })
  return { api, stop, update }
}

afterEach(() => {
  delete document.documentElement.dataset.reduceMotion
  vi.useRealTimers()
})

it('acknowledges a cursor move at once when motion is reduced and only once per sequence', () => {
  const { api, stop, update } = mountWithCursor('on')
  update({ cursor: { visible: true } })
  update({ cursor: { visible: true, x: 300, y: 200, moveSequence: 4, animate: true } })
  update({ cursor: { visible: true, x: 300, y: 200, moveSequence: 4, animate: true } })
  expect(api.cursorArrived).toHaveBeenCalledTimes(1)
  expect(api.cursorArrived).toHaveBeenCalledWith({ tabId: 'tab', moveSequence: 4 })
  stop()
})

it('acknowledges a hidden cursor move without animating', () => {
  const { api, stop, update } = mountWithCursor('off')
  update({ visible: false, cursor: { visible: true, x: 300, y: 200, moveSequence: 9, animate: false } })
  expect(api.cursorArrived).toHaveBeenCalledWith({ tabId: 'tab', moveSequence: 9 })
  stop()
})

it('acknowledges an animated cursor move only after it arrives', async () => {
  vi.useFakeTimers({ toFake: ['requestAnimationFrame', 'cancelAnimationFrame', 'performance'] })
  const { api, stop, update } = mountWithCursor('off')
  update({ cursor: { visible: true } })
  update({ cursor: { visible: true, x: 700, y: 500, moveSequence: 12, animate: true } })
  expect(api.cursorArrived).not.toHaveBeenCalled()
  await vi.advanceTimersByTimeAsync(5000)
  expect(api.cursorArrived).toHaveBeenCalledTimes(1)
  expect(api.cursorArrived).toHaveBeenCalledWith({ tabId: 'tab', moveSequence: 12 })
  stop()
})

import { afterEach, expect, it, vi } from 'vitest'
import { startBrowserGuestHost } from '../browser/browserGuestHost'
import type { BrowserHostApi, BrowserHostDescriptor, BrowserHostEvent } from '../../shared/viewer/browserHost'

it('retains guest nodes and navigation while switching visibility and ignores stale bootstrap records', async () => {
  let listener!: (event: BrowserHostEvent) => void
  let initial!: (hosts: BrowserHostDescriptor[]) => void
  const api: BrowserHostApi = {
    list: () => new Promise(resolve => { initial = resolve }),
    cursorArrived: vi.fn().mockResolvedValue(undefined),
    onEvent: callback => { listener = callback; return vi.fn() }
  }
  const stop = startBrowserGuestHost(api)
  const host: BrowserHostDescriptor = { tabId: 'tab', bootstrapToken: 'bootstrap', partition: 'persist:example', automation: true,
    visible: false, bounds: { x: 10, y: 30, width: 800, height: 600 } }
  listener({ type: 'update', host })
  const guest = document.querySelector('webview') as Electron.WebviewTag
  expect(guest.getAttribute('src')).toBe('about:blank#dotcraft-browser-host=bootstrap')
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

function mountAgentGuest() {
  let listener!: (event: BrowserHostEvent) => void
  const api: BrowserHostApi = {
    list: () => new Promise(() => {}),
    cursorArrived: vi.fn().mockResolvedValue(undefined),
    onEvent: callback => { listener = callback; return vi.fn() }
  }
  const composer = document.createElement('textarea')
  document.body.append(composer)
  const stop = startBrowserGuestHost(api)
  const host: BrowserHostDescriptor = { tabId: 'tab', bootstrapToken: 'bootstrap', partition: 'persist:example', automation: true,
    visible: true, bounds: { x: 0, y: 0, width: 800, height: 600 } }
  listener({ type: 'update', host })
  const page = document.querySelector('webview') as HTMLElement
  page.tabIndex = 0
  composer.focus()
  return {
    composer, page,
    pointerDown: (source: 'user' | 'automation') => listener({ type: 'pointer-down', tabId: 'tab', source }),
    setAgentControlled: (automation: boolean) => listener({ type: 'update', host: { ...host, automation } }),
    stop: () => { stop(); composer.remove() }
  }
}

it('returns focus to the composer when the agent focuses the page', () => {
  const { composer, page, pointerDown, stop } = mountAgentGuest()
  pointerDown('automation')
  page.focus()
  expect(document.activeElement).toBe(composer)
  stop()
})

it('lets the user focus the page while the agent controls it, whichever event arrives first', () => {
  const { composer, page, pointerDown, stop } = mountAgentGuest()
  pointerDown('user')
  page.focus()
  expect(document.activeElement).toBe(page)
  composer.focus()
  page.focus()
  expect(document.activeElement).toBe(composer)
  pointerDown('user')
  expect(document.activeElement).toBe(page)
  stop()
})

it('leaves page focus alone when the agent is not in control', () => {
  const { page, setAgentControlled, stop } = mountAgentGuest()
  setAgentControlled(false)
  page.focus()
  expect(document.activeElement).toBe(page)
  stop()
})

function mountWithCursor(reduceMotion: 'on' | 'off') {
  document.documentElement.dataset.reduceMotion = reduceMotion
  let listener!: (event: BrowserHostEvent) => void
  const api: BrowserHostApi = {
    list: () => new Promise(() => {}),
    cursorArrived: vi.fn().mockResolvedValue(undefined),
    onEvent: callback => { listener = callback; return vi.fn() }
  }
  const stop = startBrowserGuestHost(api)
  const host: BrowserHostDescriptor = { tabId: 'tab', bootstrapToken: 'bootstrap', partition: 'persist:example', automation: true,
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

import { expect, it, vi } from 'vitest'
import { startBrowserGuestHost } from '../browser/browserGuestHost'
import type { BrowserHostApi, BrowserHostDescriptor, BrowserHostEvent } from '../../shared/viewer/browserHost'

it('retains guest nodes and navigation while switching visibility and ignores stale bootstrap records', async () => {
  let listener!: (event: BrowserHostEvent) => void
  let initial!: (hosts: BrowserHostDescriptor[]) => void
  const api: BrowserHostApi = {
    list: () => new Promise(resolve => { initial = resolve }),
    bind: vi.fn().mockResolvedValue(undefined), failed: vi.fn().mockResolvedValue(undefined),
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
  expect(guest.parentElement?.dataset.presentation).toBe('background')
  listener({ type: 'remove', tabId: 'tab' })
  initial([host])
  await Promise.resolve()
  expect(document.querySelector('webview')).toBeNull()
  stop()
})

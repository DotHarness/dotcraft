import type { BrowserHostApi, BrowserHostDescriptor } from '../../shared/viewer/browserHost'
import { createBrowserCursorOverlay, type BrowserCursorOverlay } from './browserCursorOverlay'
import { layoutBrowserGuest } from './browserGuestLayout'

interface MountedGuest {
  container: HTMLDivElement
  page: Electron.WebviewTag
  cursor: BrowserCursorOverlay
}

export function startBrowserGuestHost(api: BrowserHostApi, parent: HTMLElement = document.body): () => void {
  const root = document.createElement('div')
  root.className = 'dc-browser-hosts'
  parent.append(root)
  const guests = new Map<string, MountedGuest>()
  const changed = new Set<string>()
  let stopped = false

  function update(host: BrowserHostDescriptor): void {
    if (stopped) return
    let guest = guests.get(host.tabId)
    if (!guest) {
      const container = document.createElement('div')
      const page = document.createElement('webview') as Electron.WebviewTag
      container.className = 'dc-browser-guest'
      page.setAttribute('partition', host.partition)
      page.setAttribute('src', 'about:blank')
      const onReady = () => {
        page.removeEventListener('dom-ready', onReady)
        void api.bind({ tabId: host.tabId, webContentsId: page.getWebContentsId() }).catch(error => {
          if (!stopped && guests.get(host.tabId)?.page === page) {
            void api.failed({ tabId: host.tabId, message: String(error) })
          }
        })
      }
      page.addEventListener('dom-ready', onReady)
      container.append(page)
      guest = {
        container,
        page,
        cursor: createBrowserCursorOverlay(container, moveSequence => {
          void api.cursorArrived({ tabId: host.tabId, moveSequence }).catch(() => {})
        })
      }
      guests.set(host.tabId, guest)
      root.append(container)
    }
    const { box, page: explicit, captureScale } = layoutBrowserGuest(host, { width: window.innerWidth, height: window.innerHeight })
    const { style, dataset } = guest.container
    const presented = host.visible && !host.captureSurfaceSize
    style.setProperty('--dc-browser-capture-scale', String(captureScale))
    Object.assign(style, {
      left: `${box.left}px`, top: `${box.top}px`, width: `${box.width}px`, height: `${box.height}px`
    })
    if (explicit) {
      style.setProperty('--dc-browser-page-width', `${explicit.width}px`)
      style.setProperty('--dc-browser-page-height', `${explicit.height}px`)
      style.setProperty('--dc-browser-page-scale', String(explicit.scale))
      dataset.viewport = 'explicit'
    } else {
      style.removeProperty('--dc-browser-page-width')
      style.removeProperty('--dc-browser-page-height')
      style.removeProperty('--dc-browser-page-scale')
      delete dataset.viewport
    }
    dataset.presentation = host.captureSurfaceSize ? 'capturing' : host.visible ? 'visible' : host.automation ? 'background' : 'parked'
    guest.cursor.update({
      shown: presented && host.cursor?.visible === true,
      size: host.viewport ?? host.bounds,
      cursor: host.cursor
    })
  }

  const unsubscribe = api.onEvent(event => {
    if (event.type === 'pointer-down') return
    const id = event.type === 'update' ? event.host.tabId : event.tabId
    changed.add(id)
    if (event.type === 'update') update(event.host)
    else {
      const guest = guests.get(id)
      guest?.cursor.destroy()
      guest?.container.remove()
      guests.delete(id)
    }
  })
  void api.list().then(hosts => {
    for (const host of hosts) if (!changed.has(host.tabId)) update(host)
  }).catch(() => {})
  return () => {
    stopped = true
    unsubscribe()
    for (const guest of guests.values()) guest.cursor.destroy()
    root.remove()
    guests.clear()
  }
}

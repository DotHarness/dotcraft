import type { BrowserHostApi, BrowserHostDescriptor } from '../../shared/viewer/browserHost'

interface MountedGuest {
  container: HTMLDivElement
  page: Electron.WebviewTag
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
      guest = { container, page }
      guests.set(host.tabId, guest)
      container.append(page)
      root.append(container)
    }
    const { x, y } = host.bounds
    const { width, height } = host.captureSurfaceSize ?? host.bounds
    const presented = host.visible && !host.captureSurfaceSize
    guest.container.style.setProperty('--dc-browser-capture-scale', String(host.captureSurfaceSize
      ? Math.min(1, window.innerWidth / width, window.innerHeight / height)
      : 1))
    Object.assign(guest.container.style, {
      left: `${presented ? x : 0}px`, top: `${presented ? y : 0}px`,
      width: `${width}px`, height: `${height}px`
    })
    guest.container.dataset.presentation = host.captureSurfaceSize ? 'capturing' : host.visible ? 'visible' : host.automation ? 'background' : 'parked'
  }

  const unsubscribe = api.onEvent(event => {
    if (event.type === 'pointer-down') return
    const id = event.type === 'update' ? event.host.tabId : event.tabId
    changed.add(id)
    if (event.type === 'update') update(event.host)
    else {
      guests.get(id)?.container.remove()
      guests.delete(id)
    }
  })
  void api.list().then(hosts => {
    for (const host of hosts) if (!changed.has(host.tabId)) update(host)
  }).catch(() => {})
  return () => {
    stopped = true
    unsubscribe()
    root.remove()
    guests.clear()
  }
}

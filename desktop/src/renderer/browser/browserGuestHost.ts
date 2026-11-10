import { BROWSER_GUEST_BOOTSTRAP_PREFIX, type BrowserHostApi, type BrowserHostDescriptor } from '../../shared/viewer/browserHost'
import { createBrowserCursorOverlay, type BrowserCursorOverlay } from './browserCursorOverlay'
import { layoutBrowserGuest } from './browserGuestLayout'

interface MountedGuest {
  container: HTMLDivElement
  page: Electron.WebviewTag
  cursor: BrowserCursorOverlay
  agentControlled: boolean
  userEngaged: boolean
  deflected: boolean
}

export function startBrowserGuestHost(api: BrowserHostApi, parent: HTMLElement = document.body): () => void {
  const root = document.createElement('div')
  root.className = 'dc-browser-hosts'
  parent.append(root)
  const guests = new Map<string, MountedGuest>()
  const changed = new Set<string>()
  let stopped = false
  let returnFocus: HTMLElement | null = null
  const rememberFocus = (event: FocusEvent): void => {
    if (event.target instanceof HTMLElement && event.target.tagName !== 'WEBVIEW') returnFocus = event.target
  }
  document.addEventListener('focusin', rememberFocus, true)

  // Agent clicks and DOM focus inside the guest move window focus to the webview; give it back unless the user clicked the page.
  function guardFocus(guest: MountedGuest): void {
    guest.page.addEventListener('focus', () => {
      if (!guest.agentControlled || guest.userEngaged) return
      guest.deflected = true
      if (returnFocus?.isConnected) returnFocus.focus({ preventScroll: true })
      else guest.page.blur()
    })
    guest.page.addEventListener('blur', () => { guest.userEngaged = false })
  }

  function userPointerDown(guest: MountedGuest): void {
    guest.userEngaged = true
    if (guest.deflected && document.activeElement !== guest.page) guest.page.focus()
    guest.deflected = false
  }

  function update(host: BrowserHostDescriptor): void {
    if (stopped) return
    let guest = guests.get(host.tabId)
    if (!guest) {
      const container = document.createElement('div')
      const page = document.createElement('webview') as Electron.WebviewTag
      container.className = 'dc-browser-guest'
      page.setAttribute('partition', host.partition)
      page.setAttribute('src', `${BROWSER_GUEST_BOOTSTRAP_PREFIX}${host.bootstrapToken}`)
      container.append(page)
      guest = {
        container,
        page,
        cursor: createBrowserCursorOverlay(container, moveSequence => {
          void api.cursorArrived({ tabId: host.tabId, moveSequence }).catch(() => {})
        }),
        agentControlled: false,
        userEngaged: false,
        deflected: false
      }
      guardFocus(guest)
      guests.set(host.tabId, guest)
      root.append(container)
    }
    guest.agentControlled = host.automation
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
    if (event.type === 'pointer-down') {
      const guest = guests.get(event.tabId)
      if (guest && event.source === 'user') userPointerDown(guest)
      return
    }
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
    document.removeEventListener('focusin', rememberFocus, true)
    for (const guest of guests.values()) guest.cursor.destroy()
    root.remove()
    guests.clear()
  }
}

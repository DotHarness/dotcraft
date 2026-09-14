import { useEffect } from 'react'
import { startBrowserGuestHost } from './browserGuestHost'

export function BrowserGuestHost(): null {
  useEffect(() => startBrowserGuestHost(
    window.api.workspace.viewer.browser.host,
    document.querySelector<HTMLElement>('.dotcraft-plugin-app-seat') ?? document.body
  ), [])
  return null
}

import { nativeImage, type BrowserWindow } from 'electron'
import type { BrowserEventPayload } from '../shared/viewer/types'

export interface BrowserTabRuntime {
  tabId: string
  threadId?: string
  projectKey: string
  browserStorageId: string
  page: Electron.WebContents
  desiredVisible: boolean
  boundsInitialized: boolean
  currentUrl: string
  title: string
  faviconDataUrl?: string
  automationSessionName?: string
  automationActive?: boolean
  authPopups: Set<BrowserWindow>
}

interface PageEventEffects {
  current(): boolean
  emit(payload: BrowserEventPayload): void
  history(): void
  checkpoint(): void
  destroyed(): void
}

export function bindBrowserPageEvents(tab: BrowserTabRuntime, effects: PageEventEffects): void {
  const wc = tab.page
  wc.on('did-start-loading', () => {
    if (!effects.current()) return
    effects.emit({ tabId: tab.tabId, threadId: tab.threadId, type: 'did-start-loading' })
    effects.history()
  })
  wc.on('did-stop-loading', () => {
    if (!effects.current()) return
    tab.currentUrl = wc.getURL() || tab.currentUrl
    effects.emit({ tabId: tab.tabId, threadId: tab.threadId, type: 'did-stop-loading', url: tab.currentUrl })
    effects.history()
    effects.checkpoint()
  })
  wc.on('did-navigate', (_event, url) => {
    if (!effects.current()) return
    tab.currentUrl = url
    effects.emit({ tabId: tab.tabId, threadId: tab.threadId, type: 'did-navigate', url })
    effects.history()
    effects.checkpoint()
  })
  wc.on('did-navigate-in-page', (_event, url) => {
    if (!effects.current()) return
    tab.currentUrl = url
    effects.emit({ tabId: tab.tabId, threadId: tab.threadId, type: 'did-navigate', url })
    effects.history()
    effects.checkpoint()
  })
  wc.on('did-fail-load', (_event, errorCode, errorDescription, validatedURL, isMainFrame) => {
    if (!effects.current() || !isMainFrame || errorCode === -3) return
    effects.emit({ tabId: tab.tabId, threadId: tab.threadId, type: 'did-fail-load', url: validatedURL,
      message: errorDescription, errorCode, errorDescription, validatedURL, finalURL: wc.getURL(), isMainFrame })
  })
  wc.on('page-title-updated', (event, title) => {
    if (!effects.current()) return
    event.preventDefault()
    tab.title = title || 'DotCraft Browser'
    effects.emit({ tabId: tab.tabId, threadId: tab.threadId, type: 'page-title-updated', title: tab.title })
    effects.checkpoint()
  })
  wc.on('page-favicon-updated', async (_event, favicons) => {
    const first = favicons[0]
    if (!effects.current() || !first) return
    try {
      const response = await fetch(first)
      if (!response.ok) return
      const data = Buffer.from(await response.arrayBuffer())
      if (!effects.current()) return
      const image = nativeImage.createFromBuffer(data)
      if (image.isEmpty()) return
      tab.faviconDataUrl = image.toDataURL()
      effects.emit({ tabId: tab.tabId, threadId: tab.threadId, type: 'page-favicon-updated', faviconDataUrl: tab.faviconDataUrl })
      effects.checkpoint()
    } catch {}
  })
  wc.on('render-process-gone', () => {
    if (effects.current()) effects.emit({ tabId: tab.tabId, threadId: tab.threadId, type: 'crashed' })
  })
  wc.once('destroyed', () => { if (effects.current()) effects.destroyed() })
}

import { shell, type BrowserWindow } from 'electron'
import type { BrowserEventPayload } from '../shared/viewer/types'
import { applyEmbeddedBrowserSecurity } from './browserSecurity'
import { bindBrowserPageSession, browserPageAuthority, browserSessionKind } from './browserSessionPolicy'

export interface NavigableBrowserTab {
  tabId: string
  threadId?: string
  page: Electron.WebContents
  authPopups: Set<BrowserWindow>
}
export type BrowserNavigationDecision = 'allow' | 'external-handoff' | 'blocked' | 'new-tab'

export function extractScheme(raw: string): string | null {
  try { return new URL(raw).protocol.toLowerCase() } catch { return null }
}

export function classifyBrowserUrl(url: string): BrowserNavigationDecision {
  const scheme = extractScheme(url)
  if (scheme && ['http:', 'https:', 'dotcraft-viewer:'].includes(scheme)) return 'allow'
  return scheme && ['mailto:', 'tel:'].includes(scheme) ? 'external-handoff' : 'blocked'
}

export function normalizeBrowserUrl(input: string): string | null {
  const trimmed = input.trim()
  if (!trimmed || /[\u0000-\u001f]/.test(trimmed)) return null
  const localHost = /^(localhost|127\.0\.0\.1|\[?::1\]?)(:\d+)?(\/|$)/i.test(trimmed)
  const candidate = /^[a-zA-Z][a-zA-Z\d+\-.]*:/.test(trimmed)
    ? localHost ? `http://${trimmed}` : trimmed
    : localHost ? `http://${trimmed}` : `https://${trimmed}`
  try { return new URL(candidate).toString() } catch { return null }
}

export function classifyTabNavigation(tab: NavigableBrowserTab, url: string): BrowserNavigationDecision {
  if (url === 'about:blank') return 'allow'
  const authority = browserPageAuthority(tab.page)
  const scheme = extractScheme(url)
  const decision = scheme === 'file:' && authority.allowFileScheme ? 'allow' : classifyBrowserUrl(url)
  return decision === 'allow' && browserSessionKind(url) !== authority.kind ? 'new-tab' : decision
}

function controlledPopup(details: Electron.HandlerDetails): boolean {
  const name = details.frameName.trim().toLowerCase()
  return details.disposition === 'new-window' || details.features.trim() !== ''
    || (name !== '' && !['_blank', '_self', '_parent', '_top'].includes(name))
}

export class BrowserGuestNavigation {
  constructor(private readonly emit: (win: BrowserWindow, payload: BrowserEventPayload) => void) {}

  boundary(win: BrowserWindow, tab: NavigableBrowserTab, url: string, openNewTab = true): boolean {
    const decision = classifyTabNavigation(tab, url)
    if (decision === 'allow') return false
    if (decision === 'new-tab' && openNewTab) this.emit(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'request-new-tab', url })
    else if (decision === 'external-handoff' && openNewTab) {
      void shell.openExternal(url)
      this.emit(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'external-handoff', url })
    } else this.emit(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'blocked-navigation', message: `Blocked browser navigation: ${extractScheme(url) ?? 'unknown'}` })
    return true
  }

  bind(win: BrowserWindow, tab: NavigableBrowserTab, page = tab.page): void {
    page.on('will-navigate', (event, url) => { if (this.boundary(win, tab, url)) event.preventDefault() })
    page.on('will-redirect', (event, url, _inPlace, isMainFrame) => {
      if (this.boundary(win, tab, url, isMainFrame)) event.preventDefault()
    })
    page.on('will-frame-navigate', (event) => {
      if (!event.isMainFrame && this.boundary(win, tab, event.url, false)) event.preventDefault()
    })
    page.setWindowOpenHandler((details) => this.open(win, tab, details))
    page.on('did-create-window', popup => this.bindPopup(win, tab, popup))
  }

  private open(win: BrowserWindow, tab: NavigableBrowserTab, details: Electron.HandlerDetails): Electron.WindowOpenHandlerResponse {
    const normalized = normalizeBrowserUrl(details.url)
    const popup = controlledPopup(details)
    if (popup && (details.url === 'about:blank' || (normalized && classifyTabNavigation(tab, normalized) === 'allow'))) {
      return { action: 'allow', outlivesOpener: false, overrideBrowserWindowOptions: {
        parent: win, autoHideMenuBar: true, webPreferences: applyEmbeddedBrowserSecurity({}, tab.page.session)
      } }
    }
    if (normalized) {
      const decision = classifyTabNavigation(tab, normalized)
      if (decision === 'allow' || decision === 'new-tab') this.emit(win, { tabId: tab.tabId, threadId: tab.threadId, type: 'request-new-tab', url: normalized })
      else this.boundary(win, tab, normalized)
    }
    return { action: 'deny' }
  }

  private bindPopup(win: BrowserWindow, tab: NavigableBrowserTab, popup: BrowserWindow): void {
    tab.authPopups.add(popup)
    const authority = browserPageAuthority(tab.page)
    bindBrowserPageSession(popup.webContents, authority.kind, authority.allowFileScheme)
    popup.setMenuBarVisibility(false)
    popup.webContents.setUserAgent(tab.page.getUserAgent())
    popup.once('closed', () => tab.authPopups.delete(popup))
    this.bind(win, tab, popup.webContents)
  }
}

import type { LinkOpenTarget } from '../../shared/desktopSettings'
import { bareHostname } from '../../shared/linkPreviews'
import { useLinkOpenPreferencesStore } from '../stores/linkOpenPreferencesStore'
import { addToast } from '../stores/toastStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'

type Translator = (key: string) => string

export type WebLinkDestination = 'preferred' | 'newBrowserTab' | 'systemBrowser'

const QUERY_LABEL_LENGTH = 20
const PATH_LABEL_LENGTH = 18

function isLocalWebUrl(url: string): boolean {
  const host = bareHostname(new URL(url).hostname).toLowerCase()
  return host === 'localhost' || host.endsWith('.localhost') || host === '::1' || host === '0.0.0.0' ||
    /^127(?:\.\d{1,3}){3}$/.test(host)
}

export function isWebUrl(url: string): boolean {
  return /^https?:\/\//i.test(url.trim())
}

export function linkClickDestination(event: { ctrlKey: boolean; metaKey: boolean; shiftKey: boolean; button: number }): WebLinkDestination {
  if (event.shiftKey) return 'systemBrowser'
  if (event.ctrlKey || event.metaKey || event.button === 1) return 'newBrowserTab'
  return 'preferred'
}

function preferredLinkOpenTarget(url: string): LinkOpenTarget {
  const preferences = useLinkOpenPreferencesStore.getState().preferences
  return isLocalWebUrl(url) ? preferences.local : preferences.web
}

function openUrlInBrowserTab(params: { threadId: string; url: string; forceNew: boolean }): void {
  const viewerStore = useViewerTabStore.getState()
  const ui = useUIStore.getState()
  const focused = params.forceNew ? null : viewerStore.focusBrowserTabByUrl({ threadId: params.threadId, url: params.url })
  const tabId = focused ?? viewerStore.openBrowser({
    threadId: params.threadId,
    initialUrl: params.url,
    initialLabel: params.url,
    forceNew: true
  })
  ui.setActiveViewerTab(tabId)
  ui.setDetailPanelVisible(true)
}

export async function openWebLink(params: {
  url: string
  destination: WebLinkDestination
  threadId: string | null
  t: Translator
}): Promise<boolean> {
  const inAppTarget = params.destination === 'newBrowserTab' ||
    (params.destination === 'preferred' && preferredLinkOpenTarget(params.url) === 'inAppBrowser')
  if (inAppTarget && params.threadId) {
    openUrlInBrowserTab({ threadId: params.threadId, url: params.url, forceNew: params.destination === 'newBrowserTab' })
    return true
  }
  try {
    await window.api.shell.openExternal(params.url)
    return true
  } catch {
    addToast(params.t('conversation.deepLink.rejectUnsupported'), 'warning')
    return false
  }
}

export async function copyLinkText(text: string, t: Translator): Promise<void> {
  try {
    await navigator.clipboard.writeText(text)
    addToast(t('toast.copied'), 'success')
  } catch {
    addToast(t('conversation.link.copyFailed'), 'warning')
  }
}

export function linkAddress(url: string): string {
  return decodeURIComponent(new URL(url).pathname)
}

export function shortenUrlForDisplay(rawUrl: string): string {
  try {
    const parsed = new URL(rawUrl)
    const host = parsed.port ? `${parsed.hostname}:${parsed.port}` : parsed.hostname
    const path = parsed.pathname === '/' ? '' : parsed.pathname.replace(/\/+$/, '')
    const compactPath = !path || path.length <= PATH_LABEL_LENGTH
      ? path
      : `/${path.split('/').filter(Boolean)[0] ?? ''}`
    const query = parsed.search.slice(1)
    if (!query) return `${host}${compactPath}`
    return `${host}${compactPath || '/'}?${shortenQuery(query)}`
  } catch {
    return rawUrl
  }
}

function shortenQuery(query: string): string {
  if (query.length <= QUERY_LABEL_LENGTH) return query
  const tail = query.slice(-QUERY_LABEL_LENGTH)
  const boundary = tail.indexOf('&')
  return `…${boundary >= 0 ? tail.slice(boundary + 1) : tail}`
}

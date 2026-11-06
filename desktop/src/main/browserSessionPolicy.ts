import { app, session, type BrowserWindow } from 'electron'
import { createHash } from 'node:crypto'
import { fileURLToPath } from 'node:url'
import { buildViewerUrl, installViewerProtocolHandlerForSession } from './viewerFileProtocol'
import { configureEmbeddedBrowserIdentity } from './browserIdentity'
import { normalizeWorkspaceProjectKey } from '../shared/workspaceProjectKey'

export type BrowserSessionKind = 'website' | 'local-preview'
interface PageAuthority { kind: BrowserSessionKind; allowFileScheme: boolean }
const pages = new WeakMap<Electron.WebContents, PageAuthority>()

export function browserSessionKind(url: string): BrowserSessionKind {
  return /^(?:file|dotcraft-viewer):/i.test(url) ? 'local-preview' : 'website'
}

export function partitionForWorkspace(workspacePath: string): string {
  const normalized = workspacePath.trim().toLowerCase()
  const hash = createHash('sha1').update(normalized).digest('hex').slice(0, 12)
  return `persist:dotcraft-viewer:${hash}`
}

export function bindBrowserPageSession(page: Electron.WebContents, kind: BrowserSessionKind, allowFileScheme: boolean): void {
  pages.set(page, { kind, allowFileScheme })
}

export function browserPageAuthority(page: Electron.WebContents): PageAuthority {
  return pages.get(page) ?? { kind: browserSessionKind(page.getURL()), allowFileScheme: false }
}

export function browserLoadUrl(url: string): string {
  const parsed = new URL(url)
  if (parsed.protocol !== 'file:') return url
  const target = new URL(buildViewerUrl(fileURLToPath(parsed)))
  target.hash = parsed.hash
  return target.toString()
}

export function assertBrowserPageNavigation(page: Electron.WebContents, url: string): string {
  const authority = browserPageAuthority(page)
  const protocol = new URL(url).protocol
  if (url === 'about:blank') return url
  if (browserSessionKind(url) !== authority.kind) {
    throw new Error('NavigationFailed: Website and local preview pages use separate tabs. Create a new tab for this URL.')
  }
  if (!['http:', 'https:', 'dotcraft-viewer:'].includes(protocol) && !(protocol === 'file:' && authority.allowFileScheme)) {
    throw new Error(`NavigationFailed: Blocked browser scheme: ${protocol}`)
  }
  return browserLoadUrl(url)
}

export class BrowserSessionPolicy {
  private readonly configured = new WeakSet<Electron.Session>()

  constructor(private readonly onDownload: (event: Electron.Event, item: Electron.DownloadItem, page: Electron.WebContents) => void) {}

  select(win: BrowserWindow, workspacePath: string, initialUrl: string): { partition: string; kind: BrowserSessionKind } {
    const kind = browserSessionKind(initialUrl)
    const scope = createHash('sha256').update(normalizeWorkspaceProjectKey(workspacePath)).digest('hex').slice(0, 24)
    const partition = kind === 'website' ? partitionForWorkspace(workspacePath) : `dotcraft-preview:${win.webContents.id}:${scope}`
    const target = session.fromPartition(partition)
    if (kind === 'local-preview') installViewerProtocolHandlerForSession(target, win.webContents.id, workspacePath)
    this.configure(target, kind)
    return { partition, kind }
  }

  configure(target: Electron.Session, kind: BrowserSessionKind = 'website'): void {
    if (this.configured.has(target)) return
    this.configured.add(target)
    configureEmbeddedBrowserIdentity(target, {
      appName: app.getName(), preferredLanguages: app.getPreferredSystemLanguages(), fallbackLocale: app.getLocale()
    })
    target.on('will-download', this.onDownload)
    target.setPermissionCheckHandler((_page, permission) => permission === 'clipboard-sanitized-write')
    target.setPermissionRequestHandler((_page, permission, callback) => callback(permission === 'clipboard-sanitized-write'))
    target.webRequest.onBeforeRequest({ urls: ['file://*/*', 'dotcraft-viewer://*/*', 'dotcraft-plugin://*/*'] }, (details, callback) => {
      callback({ cancel: kind === 'website' || !details.url.startsWith('dotcraft-viewer:') })
    })
  }
}

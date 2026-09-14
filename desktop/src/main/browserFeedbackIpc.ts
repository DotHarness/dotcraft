import { BrowserWindow, dialog, ipcMain, type WebContents } from 'electron'
import type { ViewerBrowserManager } from './viewerBrowser'
import { BrowserPageControls, type BrowserContextLabels } from './browserPageControls'
import { capturePageReference } from './browserPageSelection'
import type { BrowserFeedbackEvent, BrowserSelectRequest } from '../shared/viewer/browserFeedback'

export const BROWSER_FEEDBACK_CHANNELS = [
  'viewer:browser:feedback-enable', 'viewer:browser:downloads', 'viewer:browser:download-cancel',
  'viewer:browser:download-remove', 'viewer:browser:download-location', 'viewer:browser:download-location-change',
  'viewer:browser:download-open', 'viewer:browser:find', 'viewer:browser:find-close',
  'viewer:browser:zoom', 'viewer:browser:select', 'viewer:browser:selection-cancel', 'viewer:browser:inspect'
] as const

export function registerBrowserFeedbackIpc(manager: ViewerBrowserManager): void {
  const controls = new WeakMap<WebContents, BrowserPageControls>()
  const contextLabels = new WeakMap<WebContents, BrowserContextLabels>()
  function target(sender: WebContents, tabId: string) {
    const win = BrowserWindow.fromWebContents(sender)
    if (!win || win.isDestroyed()) throw new Error('Browser window not available.')
    return manager.feedbackTarget(win, tabId)
  }
  function emit(sender: WebContents, event: BrowserFeedbackEvent): void {
    if (!sender.isDestroyed()) sender.send('viewer:browser:feedback', event)
  }
  function control(sender: WebContents, tabId: string): BrowserPageControls {
    const { page } = target(sender, tabId)
    let value = controls.get(page)
    if (!value) {
      value = new BrowserPageControls(page, tabId, (event) => emit(sender, event))
      controls.set(page, value)
    }
    return value
  }
  ipcMain.handle('viewer:browser:feedback-enable', (event, params: { tabId: string; labels: BrowserContextLabels }) => {
    const origin = target(event.sender, params.tabId)
    const value = control(event.sender, params.tabId)
    const previousLabels = contextLabels.get(origin.page)
    if (previousLabels) Object.assign(previousLabels, params.labels)
    else {
      value.enableContextMenu(params.labels, (url) => event.sender.send('viewer:browser:event', {
        type: 'request-new-tab', tabId: params.tabId, threadId: origin.threadId, url
      }), () => {
        void capturePageReference(origin.page, { tabId: origin.tabId, threadId: origin.threadId }, 'text')
          .then(reference => { if (reference) emit(event.sender, { type: 'selection', reference }) })
          .catch(error => emit(event.sender, { type: 'error', tabId: params.tabId, message: String(error instanceof Error ? error.message : error) }))
      })
      contextLabels.set(origin.page, params.labels)
    }
    return { find: value.snapshot(), zoomPercent: Math.round(origin.page.getZoomFactor() * 100) }
  })
  ipcMain.handle('viewer:browser:inspect', (event, params: { tabId: string }) => target(event.sender, params.tabId).page.openDevTools({ mode: 'detach' }))
  ipcMain.handle('viewer:browser:downloads', () => manager.userDownloads().snapshot())
  ipcMain.handle('viewer:browser:download-remove', (_event, params: { id?: string }) => manager.userDownloads().remove(params.id))
  ipcMain.handle('viewer:browser:download-location', () => manager.userDownloads().location())
  ipcMain.handle('viewer:browser:download-location-change', async event => {
    const win = BrowserWindow.fromWebContents(event.sender)
    if (!win) throw new Error('Browser window not available.')
    const downloads = manager.userDownloads()
    const result = await dialog.showOpenDialog(win, { defaultPath: downloads.location(), properties: ['openDirectory', 'createDirectory'] })
    if (!result.canceled && result.filePaths[0]) downloads.setLocation(result.filePaths[0])
    return downloads.location()
  })
  ipcMain.handle('viewer:browser:download-cancel', (_event, params: { id: string }) => manager.userDownloads().cancel(params.id))
  ipcMain.handle('viewer:browser:download-open', (_event, params: { id: string }) => manager.userDownloads().open(params.id))
  ipcMain.handle('viewer:browser:find', (event, params: { tabId: string; query: string; direction?: 'next' | 'previous' }) =>
    control(event.sender, params.tabId).find(params.query, params.direction))
  ipcMain.handle('viewer:browser:find-close', (event, params: { tabId: string; restoreFocus?: boolean }) => {
    control(event.sender, params.tabId).close()
    if (params.restoreFocus !== false) target(event.sender, params.tabId).page.focus()
  })
  ipcMain.handle('viewer:browser:zoom', (event, params: { tabId: string; action: 'in' | 'out' | 'reset' }) =>
    control(event.sender, params.tabId).zoom(params.action))
  ipcMain.handle('viewer:browser:select', async (event, params: BrowserSelectRequest) => {
    const origin = target(event.sender, params.tabId)
    const reference = await capturePageReference(origin.page, { tabId: origin.tabId, threadId: origin.threadId }, params.kind, params.accent)
    if (reference) emit(event.sender, { type: 'selection', reference })
    return reference
  })
  ipcMain.handle('viewer:browser:selection-cancel', (event, params: { tabId: string }) =>
    target(event.sender, params.tabId).page.executeJavaScript('window.__dotcraftCancelSelection?.()'))
}

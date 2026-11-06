import { ipcRenderer } from 'electron'
import type { BrowserRestoreResult } from '../shared/viewer/types'

export interface BrowserCreateParams {
  tabId: string
  threadId?: string
  workspacePath: string
  projectKey?: string
  browserStorageId?: string
  initialUrl?: string
}
export interface BrowserSnapshot {
  tabId: string
  browserStorageId: string
  projectKey: string
  suspended: boolean
  currentUrl: string
  title: string
  faviconDataUrl?: string
  canGoBack: boolean
  canGoForward: boolean
  loading: boolean
}

export const viewerBrowserLifecycle = {
  create(params: BrowserCreateParams): Promise<BrowserSnapshot> {
    return ipcRenderer.invoke('viewer:browser:create', params)
  },
  restoreState(params: {
    workspacePath: string
    projectKey?: string
    threadId?: string
    tabs: Array<{ tabId: string; browserStorageId: string; initialUrl?: string }>
  }): Promise<BrowserRestoreResult[]> {
    return ipcRenderer.invoke('viewer:browser:restore-state', params)
  },
  setForegroundScope(params: { projectKey: string; threadId?: string }): Promise<void> {
    return ipcRenderer.invoke('viewer:browser:set-foreground-scope', params)
  }
}

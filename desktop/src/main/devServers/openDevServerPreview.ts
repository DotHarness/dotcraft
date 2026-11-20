import { randomUUID } from 'node:crypto'
import type { BrowserWindow } from 'electron'
import { panelScopeKey } from '../../shared/viewer/panelState'
import { BROWSER_USE_OPEN_CHANNEL, type BrowserUseOpenPayload } from '../../shared/viewer/types'
import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

const openedThreads = new WeakMap<BrowserWindow, Set<string>>()

export function openDevServerPreview(
  owner: BrowserWindow,
  projectKey: string,
  preview: { threadId: string; name: string; url: string }
): void {
  if (owner.isDestroyed() || owner.webContents.isDestroyed()) return
  const scope = { projectKey: normalizeWorkspaceProjectKey(projectKey), threadId: preview.threadId }
  if (!scope.projectKey || !scope.threadId) return
  const key = panelScopeKey(scope)
  const opened = openedThreads.get(owner) ?? new Set<string>()
  const payload: BrowserUseOpenPayload = {
    ...scope,
    tabId: randomUUID(),
    initialUrl: preview.url,
    title: preview.name,
    focusMode: opened.has(key) ? 'none' : 'first-open',
    reuseByUrl: true
  }
  owner.webContents.send(BROWSER_USE_OPEN_CHANNEL, payload)
  opened.add(key)
  openedThreads.set(owner, opened)
}

import { app, type BrowserWindow } from 'electron'
import { join } from 'node:path'
import type { PanelScope } from '../shared/viewer/panelState'
import { ThreadPanelStateStore } from './threadPanelStateStore'
import type { WindowIpcContext } from './ipcBridgeContracts'
import { viewerBrowserManager } from './viewerBrowser'
import { viewerTerminalManager } from './viewerTerminal'

let sharedStore: ThreadPanelStateStore | undefined
function store(): ThreadPanelStateStore {
  return sharedStore ??= new ThreadPanelStateStore(join(app.getPath('userData'), 'thread-panel-state.json'))
}

function requireScope(value: unknown): PanelScope {
  if (!value || typeof value !== 'object' || !('projectKey' in value) || !('threadId' in value) ||
      typeof value.projectKey !== 'string' || !value.projectKey.trim() ||
      typeof value.threadId !== 'string' || !value.threadId.trim()) throw new Error('Invalid panel scope.')
  return { projectKey: value.projectKey, threadId: value.threadId }
}

export function forgetThreadPanelState(projectKey: string, threadId: string, owner?: BrowserWindow | null): void {
  const scope = { projectKey, threadId }
  const checkpoint = store().read(scope)
  try {
    store().removeThread(scope)
  } catch {
    console.warn('[desktop] deleted panel checkpoint could not be removed')
  }
  if (!owner || owner.isDestroyed()) return
  viewerBrowserManager.destroyThreadTabs(owner, scope)
  for (const tab of checkpoint?.tabs ?? []) {
    if (tab.kind === 'terminal') viewerTerminalManager.destroyTab(owner, tab.id)
  }
}

export function registerThreadPanelStateHandlers({ handleSafe }: WindowIpcContext): void {
  handleSafe('viewer:panel-state:read', (_event, scope: unknown) => store().read(requireScope(scope)))
  handleSafe('viewer:panel-state:update', (_event, scope: unknown, checkpoint: unknown) => {
    store().update(requireScope(scope), checkpoint)
  })
}

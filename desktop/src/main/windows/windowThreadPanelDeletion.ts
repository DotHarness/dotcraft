import type { BrowserWindow } from 'electron'
import { forgetThreadPanelState } from '../threadPanelStateIpc'
import type { WorkspaceConnectionEntry } from './windowTypes'

export function handleWorkspaceThreadPanelDeletion(entry: WorkspaceConnectionEntry, method: string, params: unknown, owner: BrowserWindow | null): void {
  if (method !== 'thread/deleted' || !params || typeof params !== 'object') return
  const threadId = (params as { threadId?: unknown }).threadId
  if (typeof threadId !== 'string' || !threadId.trim()) return
  forgetThreadPanelState(entry.projectId, threadId, owner)
}

import { ipcMain } from 'electron'
import { CREATOR_MODE_LIST_CHANNEL, CREATOR_MODE_SET_CHANNEL } from '../shared/creatorMode'
import creatorGuidance from './prompts/creator.md?raw'
import type { AppSettings } from './settings'

export const CREATOR_CONTEXT_KEY = 'desktop.creator'

const threads = new Set<string>()
const revisions = new Map<string, number>()

export function loadCreatorThreads(threadIds: readonly string[] | undefined): void {
  threads.clear()
  for (const threadId of threadIds ?? []) threads.add(threadId)
}

export function setCreatorThread(threadId: string, creator: boolean): boolean {
  if (threads.has(threadId) === creator) return false
  if (creator) threads.add(threadId)
  else threads.delete(threadId)
  revisions.set(threadId, creatorContextRevision(threadId) + 1)
  return true
}

export function creatorContextRevision(threadId: string): number {
  return revisions.get(threadId) ?? 0
}

export function creatorAdditionalContext(threadId: string | null): Record<string, { kind: 'application'; value: string }> {
  return threadId && threads.has(threadId)
    ? { [CREATOR_CONTEXT_KEY]: { kind: 'application', value: creatorGuidance.trimEnd() } }
    : {}
}

export function registerCreatorModeIpc(host: {
  settings: AppSettings
  updateSharedSettings(partial: Partial<AppSettings>): Promise<void>
}): void {
  loadCreatorThreads(host.settings.creatorThreadIds)
  ipcMain.handle(CREATOR_MODE_LIST_CHANNEL, () => [...threads])
  ipcMain.handle(CREATOR_MODE_SET_CHANNEL, async (_event, params: { threadId?: unknown; creator?: unknown }) => {
    const threadId = typeof params?.threadId === 'string' ? params.threadId.trim() : ''
    if (!threadId || typeof params?.creator !== 'boolean') throw new Error('A threadId and boolean creator flag are required.')
    if (setCreatorThread(threadId, params.creator)) await host.updateSharedSettings({ creatorThreadIds: [...threads] })
  })
}

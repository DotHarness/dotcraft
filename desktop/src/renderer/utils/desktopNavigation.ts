import { useAutomationsStore } from '../stores/automationsStore'
import { useThreadStore } from '../stores/threadStore'
import { requestToolCallReveal } from '../stores/toolCallRevealStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { openWorkspaceFileViewer } from './conversationDeepLink'
import { openWorkspaceThread } from './openWorkspaceThread'
import { toAbsoluteWorkspacePath } from './workspacePaths'
import { isRemoteProjectKey } from '../../shared/workspaceProjectKey'
import { useConnectionStore } from '../stores/connectionStore'
import type { ThreadSummary } from '../types/thread'

export async function openThread(threadId: string, workspacePath?: string, options: { includeArchived?: boolean } = {}): Promise<void> {
  const foreground = useWorkspaceProjectsStore.getState()
  const foregroundWorkspacePath = isRemoteProjectKey(foreground.foregroundProjectId) ? '' : foreground.foregroundWorkspacePath
  const connectionEpoch = useConnectionStore.getState().connectionEpoch
  const selectedThreadId = useThreadStore.getState().activeThreadId
  await openWorkspaceThread({
    threadId,
    workspacePath,
    foregroundWorkspacePath,
    includeArchived: options.includeArchived,
    switchWorkspace: (nextPath) => window.api.workspace.switch(nextPath),
    setPending: (payload) => useUIStore.getState().setPendingProjectThreadOpen(payload),
    clearPending: (projectKey, pendingThreadId) =>
      useUIStore.getState().clearPendingProjectThreadOpen(projectKey, pendingThreadId),
    activateThread: async (targetThreadId) => {
      if (options.includeArchived && !useThreadStore.getState().threadList.some((thread) => thread.id === targetThreadId)) {
        const result = await window.api.appServer.sendRequest('thread/read', { threadId: targetThreadId }) as unknown as { thread: ThreadSummary }
        if (useConnectionStore.getState().connectionEpoch !== connectionEpoch
          || useWorkspaceProjectsStore.getState().foregroundProjectId !== foreground.foregroundProjectId
          || useThreadStore.getState().activeThreadId !== selectedThreadId) return
        useThreadStore.getState().upsertThreads([result.thread])
      }
      useThreadStore.getState().setActiveThreadId(targetThreadId)
      useUIStore.getState().setActiveMainView('conversation')
    }
  })
}

export async function openExternalUrl(url: string): Promise<void> {
  const { protocol } = new URL(url)
  if (protocol === 'http:' || protocol === 'https:') return window.api.shell.openExternal(url)
  if (protocol === 'dotcraft:') return window.api.shell.openDesktopLink(url)
  throw new TypeError(`openExternal does not open ${protocol} URLs`)
}

export async function openActiveThreadFile(path: string): Promise<void> {
  const thread = useThreadStore.getState().activeThread
  if (!thread) return
  const workspacePath = thread.effectiveWorkspacePath?.trim() || thread.workspacePath
  await openWorkspaceFileViewer({
    threadId: thread.id,
    workspacePath,
    absolutePath: toAbsoluteWorkspacePath(workspacePath, path),
    forceNew: false
  })
}

export function openAutomation(automationId: string): void {
  useAutomationsStore.getState().selectAutomation(automationId)
  useUIStore.getState().setActiveMainView('automations')
}

export function revealToolCall(threadId: string, callId: string): void {
  requestToolCallReveal(threadId, { callId })
  if (useThreadStore.getState().activeThreadId === threadId) useUIStore.getState().setActiveMainView('conversation')
  else void openThread(threadId)
}

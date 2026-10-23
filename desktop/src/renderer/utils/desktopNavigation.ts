import { useAutomationsStore } from '../stores/automationsStore'
import { useThreadStore } from '../stores/threadStore'
import { requestToolCallReveal } from '../stores/toolCallRevealStore'
import { useUIStore } from '../stores/uiStore'
import { useWorkspaceProjectsStore } from '../stores/workspaceProjectsStore'
import { openWorkspaceFileViewer } from './conversationDeepLink'
import { openWorkspaceThread } from './openWorkspaceThread'
import { toAbsoluteWorkspacePath } from './workspacePaths'

export async function openThread(threadId: string, workspacePath?: string): Promise<void> {
  const foregroundWorkspacePath = useWorkspaceProjectsStore.getState().foregroundWorkspacePath
  await openWorkspaceThread({
    threadId,
    workspacePath,
    foregroundWorkspacePath,
    switchWorkspace: (nextPath) => window.api.workspace.switch(nextPath),
    setPending: (payload) => useUIStore.getState().setPendingProjectThreadOpen(payload),
    clearPending: (projectKey, pendingThreadId) =>
      useUIStore.getState().clearPendingProjectThreadOpen(projectKey, pendingThreadId),
    activateThread: (targetThreadId) => {
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

import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import type { PendingProjectThreadOpen, PendingProjectThreadOpenInput } from '../stores/projectThreadNavigation'

export interface OpenWorkspaceThreadOptions {
  threadId: string
  workspacePath?: string
  foregroundWorkspacePath: string
  includeArchived?: boolean
  switchWorkspace(path: string): Promise<void | false>
  setPending(payload: PendingProjectThreadOpenInput): PendingProjectThreadOpen | null
  clearPending(projectKey: string, threadId: string, expectedRequestId: number): void
  activateThread(threadId: string): Promise<void>
}

export async function openWorkspaceThread(options: OpenWorkspaceThreadOptions): Promise<void> {
  const targetWorkspace = options.workspacePath?.trim()
  if (targetWorkspace
      && normalizeWorkspaceProjectKey(targetWorkspace) !== normalizeWorkspaceProjectKey(options.foregroundWorkspacePath)) {
    const projectKey = normalizeWorkspaceProjectKey(targetWorkspace)
    const request = options.setPending({
      projectKey, workspacePath: targetWorkspace, threadId: options.threadId,
      ...(options.includeArchived ? { includeArchived: true } : {})
    })
    if (!request) return
    let switched = false
    try {
      switched = (await options.switchWorkspace(targetWorkspace)) !== false
    } finally {
      if (!switched) options.clearPending(projectKey, options.threadId, request.requestId)
    }
    return
  }
  await options.activateThread(options.threadId)
}

import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'
import type { PendingProjectThreadOpen, PendingProjectThreadOpenInput } from '../stores/projectThreadNavigation'

export interface OpenWorkspaceThreadOptions {
  threadId: string
  workspacePath?: string
  foregroundWorkspacePath: string
  includeArchived?: boolean
  switchWorkspace(path: string): Promise<void>
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
    try {
      await options.switchWorkspace(targetWorkspace)
    } catch (error) {
      options.clearPending(projectKey, options.threadId, request.requestId)
      throw error
    }
    return
  }
  await options.activateThread(options.threadId)
}

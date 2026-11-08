import { normalizeWorkspaceProjectKey } from '../../shared/workspaceProjectKey'

export interface OpenWorkspaceThreadOptions {
  threadId: string
  workspacePath?: string
  foregroundWorkspacePath: string
  includeArchived?: boolean
  switchWorkspace(path: string): Promise<void>
  setPending(payload: { projectKey: string; workspacePath: string; threadId: string; includeArchived?: boolean }): void
  clearPending(projectKey: string, threadId: string): void
  activateThread(threadId: string): void
}

export async function openWorkspaceThread(options: OpenWorkspaceThreadOptions): Promise<void> {
  const targetWorkspace = options.workspacePath?.trim()
  if (targetWorkspace
      && normalizeWorkspaceProjectKey(targetWorkspace) !== normalizeWorkspaceProjectKey(options.foregroundWorkspacePath)) {
    const projectKey = normalizeWorkspaceProjectKey(targetWorkspace)
    options.setPending({
      projectKey, workspacePath: targetWorkspace, threadId: options.threadId,
      ...(options.includeArchived ? { includeArchived: true } : {})
    })
    try {
      await options.switchWorkspace(targetWorkspace)
    } catch (error) {
      options.clearPending(projectKey, options.threadId)
      throw error
    }
    return
  }
  options.activateThread(options.threadId)
}

import type { WindowIpcContext } from './ipcBridgeContracts'
import { mainLocale } from './ipcBridgeContracts'
import { assertWorkspaceOrChatFolder } from './ipcBridgePaths'
import { normalizeWorkspaceProjectKey, sameWorkspaceProjectKey } from '../shared/workspaceProjectKey'

export function assertBrowserScope(
  { workspacePath, callbacks, owner }: Pick<WindowIpcContext, 'workspacePath' | 'callbacks' | 'owner'>,
  params: { workspacePath: string; projectKey?: string }
): void {
  const projects = callbacks.getWorkspaceProjects?.()
  const projectKey = normalizeWorkspaceProjectKey(params.projectKey ?? params.workspacePath)
  const foregroundKey = normalizeWorkspaceProjectKey(projects?.foregroundProjectId ?? workspacePath)
  if (projectKey === foregroundKey) {
    assertWorkspaceOrChatFolder(params.workspacePath, workspacePath, mainLocale(callbacks), owner.webContents.id)
    return
  }
  const catalog = [...(projects?.projects ?? []), ...(projects?.chat ? [projects.chat] : [])]
  const known = catalog.some(project =>
    project.kind !== 'remote' && normalizeWorkspaceProjectKey(project.projectId ?? project.path) === projectKey &&
    sameWorkspaceProjectKey(project.path, params.workspacePath))
  if (!known) throw new Error('Browser scope does not belong to this window project catalog.')
}

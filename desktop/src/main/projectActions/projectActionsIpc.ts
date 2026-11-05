import type { BrowserWindow, IpcMain } from 'electron'
import type {
  AddProjectActionRequest,
  ProjectActionPreferences,
  ProjectActionRunRequest,
  ProjectSetupRunRequest,
  WriteEnvironmentRequest
} from '../../shared/projectActions'
import { ProjectActionsService } from './service'

export const PROJECT_ACTIONS_CHANGED_CHANNEL = 'project-actions:changed'

interface ProjectActionsIpcDeps {
  owner: BrowserWindow
  handleSafe(channel: string, listener: Parameters<IpcMain['handle']>[1]): void
  getPreferences(): Record<string, ProjectActionPreferences> | undefined
  updatePreferences(update: Record<string, ProjectActionPreferences>): Promise<void>
}

export function registerProjectActionsHandlers(deps: ProjectActionsIpcDeps): () => void {
  const { handleSafe, owner } = deps
  const sender = owner.webContents
  let disposed = false
  const service = new ProjectActionsService({
    readPreferences: (key) => deps.getPreferences()?.[key],
    writePreferences: async (key, preferences) => {
      if (!disposed) await deps.updatePreferences({ [key]: preferences })
    },
    notifyChanged: (subscriberIds) => {
      if (!disposed && !sender.isDestroyed() && subscriberIds.includes(sender.id)) {
        sender.send(PROJECT_ACTIONS_CHANGED_CHANNEL)
      }
    }
  })
  const dispose = (): void => {
    if (disposed) return
    disposed = true
    service.unsubscribe(sender.id)
    sender.removeListener('destroyed', dispose)
  }
  sender.once('destroyed', dispose)
  handleSafe('project-actions:list', (_event, workspacePath: string) => service.list(workspacePath))
  handleSafe('project-actions:subscribe', (_event, workspacePath: string) =>
    service.subscribe(sender.id, workspacePath)
  )
  handleSafe('project-actions:unsubscribe', (_event, workspacePath: string) =>
    service.unsubscribe(sender.id, workspacePath)
  )
  handleSafe('project-actions:select', (_event, params: { workspacePath: string; environmentId: string | null }) =>
    service.select(params?.workspacePath, params?.environmentId)
  )
  handleSafe('project-actions:list-worktree-environments', (_event, workspacePath: string) =>
    service.listWorktreeEnvironments(workspacePath)
  )
  handleSafe(
    'project-actions:select-worktree-environment',
    (_event, params: { workspacePath: string; environmentId: string | null }) =>
      service.selectWorktreeEnvironment(params?.workspacePath, params?.environmentId)
  )
  handleSafe('project-actions:prepare-run', (_event, request: ProjectActionRunRequest) => service.prepareRun(request))
  handleSafe('project-actions:prepare-setup', (_event, request: ProjectSetupRunRequest) =>
    service.prepareSetup(request)
  )
  handleSafe('project-actions:add', (_event, request: AddProjectActionRequest) => service.add(request))
  handleSafe('project-actions:list-project-environments', (_event, projectPath: string) =>
    service.listProjectEnvironments(projectPath)
  )
  handleSafe('project-actions:read-environment', (_event, params: { projectPath: string; path: string }) =>
    service.readEnvironment(params)
  )
  handleSafe('project-actions:write-environment', (_event, request: WriteEnvironmentRequest) =>
    service.writeEnvironment(request)
  )
  return dispose
}

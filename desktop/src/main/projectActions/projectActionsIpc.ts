import { ipcMain, webContents, type IpcMainInvokeEvent } from 'electron'
import type {
  AddProjectActionRequest,
  ProjectActionPreferences,
  ProjectActionRunRequest,
  ProjectSetupRunRequest,
  WriteEnvironmentRequest
} from '../../shared/projectActions'
import { ProjectActionsService } from './service'

export const PROJECT_ACTIONS_CHANGED_CHANNEL = 'project-actions:changed'

const CHANNELS = [
  'project-actions:list',
  'project-actions:subscribe',
  'project-actions:unsubscribe',
  'project-actions:select',
  'project-actions:list-worktree-environments',
  'project-actions:select-worktree-environment',
  'project-actions:prepare-run',
  'project-actions:prepare-setup',
  'project-actions:add',
  'project-actions:list-project-environments',
  'project-actions:read-environment',
  'project-actions:write-environment'
] as const

interface ProjectActionsIpcDeps {
  handleSafe(channel: string, listener: Parameters<typeof ipcMain.handle>[1]): void
  getPreferences(): Record<string, ProjectActionPreferences> | undefined
  updatePreferences(update: Record<string, ProjectActionPreferences>): Promise<void>
}

let current: ProjectActionsIpcDeps | null = null
const trackedSenders = new Set<number>()

const service = new ProjectActionsService({
  readPreferences: (key) => current?.getPreferences()?.[key],
  writePreferences: async (key, preferences) => {
    await current?.updatePreferences({ [key]: preferences })
  },
  notifyChanged: (subscriberIds) => {
    for (const id of subscriberIds) {
      const contents = webContents.fromId(id)
      if (contents && !contents.isDestroyed()) contents.send(PROJECT_ACTIONS_CHANGED_CHANNEL)
    }
  }
})

function trackSender(event: IpcMainInvokeEvent): number {
  const sender = event.sender
  if (!trackedSenders.has(sender.id)) {
    trackedSenders.add(sender.id)
    sender.once('destroyed', () => {
      trackedSenders.delete(sender.id)
      service.unsubscribe(sender.id)
    })
  }
  return sender.id
}

export function registerProjectActionsHandlers(deps: ProjectActionsIpcDeps): void {
  current = deps
  const { handleSafe } = deps
  handleSafe('project-actions:list', (_event, workspacePath: string) => service.list(workspacePath))
  handleSafe('project-actions:subscribe', (event, workspacePath: string) => {
    service.subscribe(trackSender(event), workspacePath)
  })
  handleSafe('project-actions:unsubscribe', (event, workspacePath: string) => {
    service.unsubscribe(event.sender.id, workspacePath)
  })
  handleSafe('project-actions:select', (_event, params: { workspacePath: string; environmentId: string | null }) =>
    service.select(params?.workspacePath, params?.environmentId))
  handleSafe('project-actions:list-worktree-environments', (_event, workspacePath: string) =>
    service.listWorktreeEnvironments(workspacePath))
  handleSafe('project-actions:select-worktree-environment', (_event, params: { workspacePath: string; environmentId: string | null }) =>
    service.selectWorktreeEnvironment(params?.workspacePath, params?.environmentId))
  handleSafe('project-actions:prepare-run', (_event, request: ProjectActionRunRequest) =>
    service.prepareRun(request))
  handleSafe('project-actions:prepare-setup', (_event, request: ProjectSetupRunRequest) =>
    service.prepareSetup(request))
  handleSafe('project-actions:add', (_event, request: AddProjectActionRequest) => service.add(request))
  handleSafe('project-actions:list-project-environments', (_event, projectPath: string) =>
    service.listProjectEnvironments(projectPath))
  handleSafe('project-actions:read-environment', (_event, params: { projectPath: string; path: string }) =>
    service.readEnvironment(params))
  handleSafe('project-actions:write-environment', (_event, request: WriteEnvironmentRequest) =>
    service.writeEnvironment(request))
}

export function unregisterProjectActionsHandlers(): void {
  for (const channel of CHANNELS) ipcMain.removeHandler(channel)
}

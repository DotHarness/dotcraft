import type {
  DesktopPluginProjectActionsSnapshot,
  DesktopPluginSavedProjectAction
} from '@dotcraft/plugin'

import { translate } from '../../shared/locales'
import { projectActionTerminalTabId, type ProjectEnvironmentsResult } from '../../shared/projectActions'
import { requestAddProjectActionDialog } from '../components/projectActions/AddProjectActionDialog'
import { useConversationStore } from '../stores/conversationStore'
import { useUIStore } from '../stores/uiStore'
import { useViewerTabStore } from '../stores/viewerTabStore'
import { readDesktopPluginEnvironment } from './desktopPluginEnvironment'

function localWorkspace(): string {
  const { workspacePath, remoteWorkspaceActive } = useConversationStore.getState()
  if (remoteWorkspaceActive) throw new Error('Project actions are not available in a remote workspace')
  if (!workspacePath) throw new Error('No workspace is open')
  return workspacePath
}

async function readEnvironments(workspacePath: string): Promise<ProjectEnvironmentsResult> {
  localWorkspace()
  return window.api.projectActions.list(workspacePath)
}

function toSnapshot(result: ProjectEnvironmentsResult): DesktopPluginProjectActionsSnapshot {
  return {
    environments: result.environments.map(({ revision: _revision, ...environment }) => environment),
    selectedEnvironmentId: result.selectedEnvironmentId
  }
}

export async function listDesktopPluginProjectActions(workspacePath: string): Promise<DesktopPluginProjectActionsSnapshot> {
  return toSnapshot(await readEnvironments(workspacePath))
}

export function onDesktopPluginProjectActionsChange(
  workspacePath: string,
  listener: (snapshot: DesktopPluginProjectActionsSnapshot) => void
): () => void {
  if (useConversationStore.getState().remoteWorkspaceActive) return () => {}
  let delivered: string | null = null
  let sequence = 0
  let disposed = false
  void listDesktopPluginProjectActions(workspacePath).then((snapshot) => {
    delivered ??= JSON.stringify(snapshot)
  }, () => {})
  const unwatch = window.api.projectActions.watch(workspacePath, () => {
    const request = ++sequence
    void listDesktopPluginProjectActions(workspacePath).then((snapshot) => {
      const json = JSON.stringify(snapshot)
      if (disposed || request !== sequence || json === delivered) return
      delivered = json
      listener(snapshot)
    }, () => {})
  })
  return () => {
    disposed = true
    unwatch()
  }
}

export async function selectDesktopPluginProjectEnvironment(workspacePath: string, environmentId: string): Promise<void> {
  localWorkspace()
  await window.api.projectActions.select({ workspacePath, environmentId })
}

export async function runDesktopPluginProjectAction(request: {
  threadId: string
  environmentId: string
  actionId: string
}): Promise<void> {
  const run = await window.api.projectActions.prepareRun({
    workspacePath: localWorkspace(),
    environmentId: request.environmentId,
    actionId: request.actionId
  })
  const tabId = useViewerTabStore.getState().runInTerminal({
    threadId: request.threadId,
    tabId: projectActionTerminalTabId(request.threadId, request.environmentId, request.actionId),
    cwd: run.cwd,
    title: run.title,
    command: run.command
  })
  useUIStore.getState().setActiveViewerTab(tabId)
}

export async function openDesktopPluginProjectTerminal(request: { threadId: string }): Promise<void> {
  const tabId = useViewerTabStore.getState().openTerminal({
    threadId: request.threadId,
    cwd: localWorkspace(),
    initialLabel: translate(readDesktopPluginEnvironment().locale, 'viewer.newTerminalTab')
  })
  useUIStore.getState().setActiveViewerTab(tabId)
}

export async function promptDesktopPluginProjectAction(
  request: { workspacePath: string; environmentId?: string },
  ownDismiss: (dismiss: () => void) => () => void
): Promise<DesktopPluginSavedProjectAction | null> {
  const { environments, selectedEnvironmentId } = await readEnvironments(request.workspacePath)
  const environmentId = request.environmentId ?? selectedEnvironmentId
  const environment = environments.find((candidate) => candidate.id === environmentId)
  if (environmentId !== null && !environment) throw new Error(`Environment not found: ${environmentId}`)
  if (environment?.error) throw new Error(environment.error)
  const dialog = requestAddProjectActionDialog({
    workspacePath: request.workspacePath,
    environmentId: environment?.id,
    revision: environment?.revision ?? null
  })
  const dismiss = ownDismiss(dialog.dismiss)
  const saved = await dialog.result.finally(dismiss)
  return saved?.action ? { environmentId: saved.environmentId, action: saved.action } : null
}

import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { normalizeWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import { useWorkspaceProjectsStore } from '../../stores/workspaceProjectsStore'
import { beginThreadNavigation, isThreadNavigationCurrent } from '../../stores/projectThreadNavigation'
import { useSshMachinesStore } from '../../stores/sshMachinesStore'
import { useThreadStore } from '../../stores/threadStore'
import { addToast } from '../../stores/toastStore'
import { useUIStore } from '../../stores/uiStore'
import { isRemoteProject, projectIdentity } from './projectThreads'
import { canOpenSshProject, deviceProjectRef, openDeviceProject, sshProjectRef } from './sshRemoteProject'

export async function openProjectConnection(
  project: WorkspaceProjectSummary,
  active = project.state === 'foreground'
): Promise<boolean> {
  if (active) return true
  try {
    const device = deviceProjectRef(project)
    const ssh = sshProjectRef(project)
    if (device) {
      await openDeviceProject(device)
    } else if (ssh) {
      const machines = useSshMachinesStore.getState()
      if (!canOpenSshProject(machines.machines.find(machine => machine.id === ssh.machineId))) return false
      await machines.openProject(ssh.machineId, ssh.projectId)
    } else if (!isRemoteProject(project)) {
      await window.api.workspace.switch(project.path)
    } else if (project.connectionKey && project.loaded) {
      await window.api.workspace.switch(project.path, project.connectionKey)
    } else {
      return false
    }
    return true
  } catch (error) {
    addToast(error instanceof Error ? error.message : String(error), 'error')
    return false
  }
}

export async function openProjectThread(project: WorkspaceProjectSummary, threadId: string): Promise<void> {
  if (project.state !== 'foreground') {
    const projectKey = projectIdentity(project)
    const request = useUIStore.getState().setPendingProjectThreadOpen({ projectKey, workspacePath: project.path, threadId })
    if (!request) return
    if (!await openProjectConnection(project)) {
      useUIStore.getState().clearPendingProjectThreadOpen(projectKey, threadId, request.requestId)
    }
    return
  }
  useUIStore.getState().setActiveMainView('conversation')
  useThreadStore.getState().setActiveThreadId(threadId)
}

function findThreadProject(threadId: string, workspacePath?: string): WorkspaceProjectSummary | null {
  const { projects, chat } = useWorkspaceProjectsStore.getState()
  const candidates = chat ? [...projects, chat] : projects
  if (workspacePath) {
    const key = normalizeWorkspaceProjectKey(workspacePath)
    return candidates.find((project) =>
      normalizeWorkspaceProjectKey(project.path) === key || normalizeWorkspaceProjectKey(projectIdentity(project)) === key
    ) ?? null
  }
  return candidates.find((project) => (project.threads ?? []).some((thread) => (thread as { id?: unknown }).id === threadId)) ?? null
}

export async function openThread(threadId: string, workspacePath?: string): Promise<void> {
  let project = findThreadProject(threadId, workspacePath)
  const currentThreadId = useThreadStore.getState().activeThreadId
  if (!project && !workspacePath && currentThreadId && currentThreadId !== threadId) {
    const requestId = beginThreadNavigation()
    const linkedWorkspace = await window.api.threadTasks.linkedWorkspace(currentThreadId, threadId).catch(() => null)
    if (!isThreadNavigationCurrent(requestId) || useThreadStore.getState().activeThreadId !== currentThreadId) return
    if (linkedWorkspace) project = findThreadProject(threadId, linkedWorkspace)
  }
  if (project) return openProjectThread(project, threadId)
  useUIStore.getState().setActiveMainView('conversation')
  useThreadStore.getState().setActiveThreadId(threadId)
}

export async function newProjectChat(project: WorkspaceProjectSummary, active: boolean): Promise<void> {
  if (!await openProjectConnection(project, active)) return
  useUIStore.getState().goToNewChat({ workspacePath: projectIdentity(project) })
}

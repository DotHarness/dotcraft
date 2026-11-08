import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
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
    useUIStore.getState().setPendingProjectThreadOpen({ projectKey, workspacePath: project.path, threadId })
    if (!await openProjectConnection(project)) {
      useUIStore.getState().clearPendingProjectThreadOpen(projectKey, threadId)
    }
    return
  }
  useUIStore.getState().setActiveMainView('conversation')
  useThreadStore.getState().setActiveThreadId(threadId)
}

export async function newProjectChat(project: WorkspaceProjectSummary, active: boolean): Promise<void> {
  if (!await openProjectConnection(project, active)) return
  useUIStore.getState().goToNewChat({ workspacePath: projectIdentity(project) })
}

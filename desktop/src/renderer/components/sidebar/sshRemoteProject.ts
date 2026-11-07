import { useEffect } from 'react'

import { useSshMachinesStore } from '../../stores/sshMachinesStore'
import type { SshMachineView } from '../../../shared/sshMachines'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'

export interface SshProjectRef {
  machineId: string
  projectId: string
}

export function sshProjectRef(project: WorkspaceProjectSummary): SshProjectRef | null {
  const remote = project.kind === 'remote' ? project.remote : undefined
  if (remote?.source !== 'ssh' || !remote.hostId || !remote.remoteProjectId) return null
  return { machineId: remote.hostId, projectId: remote.remoteProjectId }
}

export interface DeviceProjectRef {
  computerId: string
  projectId: string
}

export function deviceProjectRef(project: WorkspaceProjectSummary): DeviceProjectRef | null {
  const remote = project.kind === 'remote' ? project.remote : undefined
  if (remote?.source !== 'device' || !remote.hostId || !remote.remoteProjectId) return null
  return { computerId: remote.hostId, projectId: remote.remoteProjectId }
}

export async function openDeviceProject(ref: DeviceProjectRef): Promise<void> {
  const result = await window.api.pairedComputers.openProject(ref.computerId, ref.projectId)
  if (!result.ok) throw new Error(result.message ?? 'The project could not be opened.')
}

export function useSshProjectMachine(ref: SshProjectRef | null): SshMachineView | undefined {
  const machineId = ref?.machineId
  useEffect(() => {
    if (machineId) useSshMachinesStore.getState().ensureLoaded()
  }, [machineId])
  return useSshMachinesStore((state) =>
    machineId ? state.machines.find((machine) => machine.id === machineId) : undefined
  )
}

export function canOpenSshProject(machine: SshMachineView | undefined): boolean {
  return machine?.status.kind !== 'unsupported'
}

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

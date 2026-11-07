import { useCallback, useEffect, useMemo, useState, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { connectPairedComputers, usePairedComputersStore } from '../../stores/pairedComputersStore'
import { useSshMachinesStore } from '../../stores/sshMachinesStore'
import { addToast } from '../../stores/toastStore'
import { canAddProject } from '../settings/panels/ssh/sshMachinePresentation'
import { openDeviceProject } from '../sidebar/sshRemoteProject'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { ProjectDialog, type FolderEntry, type ProjectDialogResult } from './ProjectDialog'

/**
 * Shared by the sidebar projects rail and the composer project selector so both
 * surfaces stay consistent. Attached folders are ordered, primary first; with no
 * folders, a name alone creates a new `<Documents>/<name>` git repository.
 */
export interface AddProjectFlow {
  beginCreate(): void
  /** Opens the Edit project dialog for a local project. `active` = it is the foreground workspace. */
  beginEdit(project: WorkspaceProjectSummary, active: boolean): void
  /** The dialog element (rendered via portal) or null when closed. */
  dialog: JSX.Element | null
  busy: boolean
}

interface DialogState {
  mode: 'create' | 'edit'
  name: string
  folders: FolderEntry[]
  project: WorkspaceProjectSummary | null
  active: boolean
}

function toFolderEntries(project: WorkspaceProjectSummary): FolderEntry[] {
  const secondary = project.secondaryFolders ?? []
  return [
    { id: 'primary', path: project.primaryFolder ?? project.path },
    ...secondary.map((path, index) => ({ id: `secondary-${index}`, path }))
  ]
}

function errorMessage(err: unknown): string {
  return err instanceof Error ? err.message : String(err)
}

export function useAddProjectFlow(): AddProjectFlow {
  const t = useT()
  const [state, setState] = useState<DialogState | null>(null)
  const [busy, setBusy] = useState(false)
  const creating = state?.mode === 'create'
  const computers = usePairedComputersStore((store) => store.computers)
  const machines = useSshMachinesStore((store) => store.machines)

  useEffect(() => {
    if (!creating) return
    useSshMachinesStore.getState().ensureLoaded()
    return connectPairedComputers()
  }, [creating])

  const remoteDevices = useMemo(() => [
    ...computers
      .filter((computer) => computer.status === 'connected')
      .map((computer) => ({ id: `device:${computer.computerId}`, label: computer.name })),
    ...machines.filter(canAddProject).map((machine) => ({ id: `ssh:${machine.id}`, label: machine.name }))
  ], [computers, machines])

  const beginCreate = useCallback((): void => {
    setState({ mode: 'create', name: '', folders: [], project: null, active: false })
  }, [])

  const beginEdit = useCallback((project: WorkspaceProjectSummary, active: boolean): void => {
    setState({ mode: 'edit', name: project.name ?? '', folders: toFolderEntries(project), project, active })
  }, [])

  const close = useCallback((): void => {
    setState((current) => (busy ? current : null))
  }, [busy])

  const submit = useCallback(async (result: ProjectDialogResult): Promise<void> => {
    if (!state) return
    setBusy(true)
    try {
      const secondaryFolders = result.folders.slice(1).map((f) => f.path)
      if (state.mode === 'create') {
        if (result.folders.length === 0) {
          const { path, gitInitialized } = await window.api.workspace.createLocalProject({ name: result.name.trim() })
          if (!gitInitialized) addToast(t('addProject.gitUnavailable'), 'warning')
          await window.api.workspace.switch(path)
        } else {
          const { path } = await window.api.workspace.saveLocalProject({
            primaryFolder: result.folders[0].path,
            secondaryFolders,
            name: result.name.trim() || undefined
          })
          await window.api.workspace.switch(path)
        }
      } else if (state.project) {
        await window.api.workspace.saveLocalProject({
          projectPath: state.project.identityWorkspacePath ?? state.project.path,
          primaryFolder: result.folders[0].path,
          secondaryFolders,
          name: result.name.trim() || undefined
        })
      }
      setState(null)
    } catch (err) {
      const key = state.mode === 'create' ? 'addProject.createFailed' : 'addProject.saveFailed'
      addToast(t(key, { error: errorMessage(err) }), 'error')
    } finally {
      setBusy(false)
    }
  }, [state, t])

  const submitRemote = useCallback(async (deviceId: string, path: string): Promise<void> => {
    setBusy(true)
    try {
      const separator = deviceId.indexOf(':')
      const hostId = deviceId.slice(separator + 1)
      if (deviceId.startsWith('device:')) {
        const result = await window.api.pairedComputers.addFolder(hostId, path)
        if (!result.ok) throw new Error(t('settings.otherDevices.folder.failure.title'))
        await openDeviceProject({ computerId: hostId, projectId: result.value.projectId })
      } else {
        const ssh = useSshMachinesStore.getState()
        const project = await ssh.addProject(hostId, path)
        await ssh.openProject(hostId, project.id)
      }
      setState(null)
    } catch (err) {
      addToast(t('addProject.createFailed', { error: errorMessage(err) }), 'error')
    } finally {
      setBusy(false)
    }
  }, [t])

  const removeProject = useCallback(async (): Promise<void> => {
    if (!state?.project) return
    setBusy(true)
    try {
      await window.api.workspace.removeProject(state.project.path)
      setState(null)
    } catch (err) {
      addToast(t('addProject.openFailed', { error: errorMessage(err) }), 'error')
    } finally {
      setBusy(false)
    }
  }, [state, t])

  const dialog = state ? (
    <ProjectDialog
      mode={state.mode}
      remoteDevices={remoteDevices}
      onSubmitRemote={(deviceId, path) => { void submitRemote(deviceId, path) }}
      initialName={state.name}
      initialFolders={state.folders}
      busy={busy}
      removeProjectDisabled={state.active}
      onSubmit={(result) => { void submit(result) }}
      onRemoveProject={state.mode === 'edit' ? () => { void removeProject() } : undefined}
      onClose={close}
    />
  ) : null

  return { beginCreate, beginEdit, dialog, busy }
}

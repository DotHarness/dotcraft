import { useCallback, useState, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { sameWorkspaceProjectKey } from '../../../shared/workspaceProjectKey'
import type { WorkspaceProjectSummary } from '../../../shared/workspaceProjects'
import { ProjectDialog, type FolderEntry, type ProjectDialogResult } from './ProjectDialog'

/**
 * Shared "Create / Edit project" flow used by the sidebar projects rail and the
 * composer project selector so both surfaces stay consistent.
 *
 * A single unified dialog replaces the old two-option add menu:
 *  - attach folders → the project uses those existing folders (primary first);
 *  - type only a name → a new `<Documents>/<name>` git repository is created;
 *  - a blank name defaults to the primary folder's name.
 *
 * Edit reuses the same dialog to manage a local project's ordered source folders.
 */
export interface AddProjectFlow {
  /** Opens the unified Create project dialog. */
  beginCreate(): void
  /** Opens the Edit project dialog for a local project. `active` = it is the foreground workspace. */
  beginEdit(project: WorkspaceProjectSummary, active: boolean): void
  /** The dialog element (rendered via portal) or null when closed. */
  dialog: JSX.Element | null
  /** True while a create/save/remove operation is in flight. */
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
  const secondary = Array.isArray(project.secondaryFolders) ? project.secondaryFolders : []
  return [
    { id: 'primary', path: project.path },
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
        const primaryFolder = result.folders[0].path
        const primaryChanged = !sameWorkspaceProjectKey(primaryFolder, state.project.path)
        await window.api.workspace.saveLocalProject({
          previousPath: state.project.path,
          primaryFolder,
          secondaryFolders,
          name: result.name.trim() || undefined
        })
        // The active project's live connection points at the old primary; re-open
        // the new primary so the foreground workspace follows the change.
        if (state.active && primaryChanged) {
          await window.api.workspace.switch(primaryFolder)
        }
      }
      setState(null)
    } catch (err) {
      const key = state.mode === 'create' ? 'addProject.createFailed' : 'addProject.saveFailed'
      addToast(t(key, { error: errorMessage(err) }), 'error')
    } finally {
      setBusy(false)
    }
  }, [state, t])

  const removeProject = useCallback(async (): Promise<void> => {
    if (!state?.project) return
    setBusy(true)
    try {
      await window.api.workspace.removeRecent(state.project.path)
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

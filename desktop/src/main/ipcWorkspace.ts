import { dialog } from 'electron'
import { promises as fs } from 'fs'
import { PROJECT_CREATION_CONFIRM_RESPOND_CHANNEL } from '../shared/projectCreationConfirm'
import type { WorkspaceSwitchOptions } from '../shared/workspaceProjects'
import type {
  WorkspaceSetupModelListRequest,
  WorkspaceSetupRequest,
  WorkspaceSetupSignInResult
} from '../shared/workspaceSetup'
import { registerChatFoldersHandlers } from './chatFoldersIpc'
import { type WindowIpcContext } from './ipcBridgeContracts'
import { registerLinkPreviewHandlers } from './linkPreviews'
import { createNewLocalProjectFolder } from './localProjectOperations'

export function registerWorkspaceHandlers(context: WindowIpcContext): void {
  const { handleSafe, callbacks, owner } = context
  handleSafe('workspace:pick-folder', async (_event, options?: { title?: string }) => {
    const result = await dialog.showOpenDialog(owner, {
      title: options?.title?.trim() || 'Select Workspace Folder',
      properties: ['openDirectory', 'createDirectory']
    })
    if (result.canceled || result.filePaths.length === 0) return null
    return result.filePaths[0]
  })

  registerChatFoldersHandlers(handleSafe, () => callbacks.getSettings())
  registerLinkPreviewHandlers(handleSafe)

  handleSafe('workspace:create-local-project', async (_event, params: { name?: string }) => {
    return createNewLocalProjectFolder(typeof params?.name === 'string' ? params.name : '')
  })

  handleSafe(
    PROJECT_CREATION_CONFIRM_RESPOND_CHANNEL,
    (_event, params: { requestId?: unknown; confirmed?: unknown }) => {
      const requestId = typeof params?.requestId === 'string' ? params.requestId.trim() : ''
      if (!requestId || typeof params?.confirmed !== 'boolean') {
        throw new Error('A requestId and boolean confirmed flag are required.')
      }
      return callbacks.respondProjectCreationConfirm?.(requestId, params.confirmed) === true
    }
  )

  handleSafe('workspace:switch', (_event, newPath: string, connectionKey?: string, options?: WorkspaceSwitchOptions) =>
    callbacks.onSwitchWorkspace(newPath, connectionKey, options)
  )

  handleSafe('workspace:clear-selection', async () => {
    await callbacks.onClearWorkspaceSelection()
  })

  handleSafe('workspace:get-projects', () => {
    return (
      callbacks.getWorkspaceProjects?.() ?? {
        foregroundWorkspacePath: '',
        foregroundProjectId: '',
        secondaryLimit: 8,
        projects: []
      }
    )
  })

  handleSafe('workspace:remove-project', async (_event, workspacePath: string) => {
    await callbacks.removeProject?.(workspacePath)
  })

  handleSafe(
    'workspace:save-local-project',
    async (
      _event,
      params: {
        projectPath?: string
        primaryFolder: string
        secondaryFolders: string[]
        name?: string
      }
    ) => {
      const primaryFolder = typeof params?.primaryFolder === 'string' ? params.primaryFolder.trim() : ''
      if (!primaryFolder) {
        throw new Error('A primary folder is required to save a project.')
      }
      let isDirectory = false
      try {
        isDirectory = (await fs.stat(primaryFolder)).isDirectory()
      } catch {
        isDirectory = false
      }
      if (!isDirectory) {
        throw new Error(`Primary folder does not exist: ${primaryFolder}`)
      }
      const projectPath = typeof params?.projectPath === 'string' && params.projectPath.trim()
        ? params.projectPath.trim()
        : primaryFolder
      await callbacks.saveLocalProject?.({
        projectPath,
        primaryFolder,
        secondaryFolders: Array.isArray(params?.secondaryFolders) ? params.secondaryFolders : [],
        name: typeof params?.name === 'string' ? params.name : undefined
      })
      return { path: projectPath }
    }
  )

  handleSafe('workspace:restart', async (_event, workspacePath: string) => {
    await callbacks.restartWorkspace?.(workspacePath)
  })

  handleSafe('workspace:stop', async (_event, workspacePath: string) => {
    await callbacks.stopWorkspace?.(workspacePath)
  })

  handleSafe('workspace:start-project', async (_event, workspacePath: string) => {
    await callbacks.startProject?.(workspacePath)
  })

  handleSafe('workspace:archive-thread', async (_event, params: { workspacePath: string; threadId: string; connectionKey?: string }) => {
    await callbacks.archiveThreadInWorkspace?.(params.workspacePath, params.threadId, params.connectionKey)
  })

  handleSafe('workspace:disconnect-remote', async (_event, projectId?: string) => {
    await callbacks.onDisconnectRemoteProject?.(projectId)
  })

  handleSafe('workspace:get-status', () => {
    return callbacks.getWorkspaceStatus()
  })

  handleSafe('workspace:run-setup', async (_event, request: WorkspaceSetupRequest) => {
    await callbacks.onRunWorkspaceSetup(request)
  })

  handleSafe('workspace:list-setup-models', async (_event, request: WorkspaceSetupModelListRequest) => {
    return callbacks.onListSetupModels(request)
  })

  handleSafe('workspace:login-setup-provider', async (_event, request: WorkspaceSetupModelListRequest) => {
    if (!callbacks.onLoginSetupProvider) return { kind: 'error' } satisfies WorkspaceSetupSignInResult
    return callbacks.onLoginSetupProvider(request)
  })

  handleSafe('workspace:open-new-window', () => {
    callbacks.onOpenNewWindow()
  })

  handleSafe('workspace:check-lock', (_event, wsPath: string) => {
    return { locked: false, ...(callbacks.isWorkspaceOpen?.(wsPath) ? { pid: process.pid } : {}) }
  })
}

import { app, dialog } from 'electron'
import { existsSync, promises as fs } from 'fs'
import * as path from 'path'
import type {
  WorkspaceSetupModelListRequest,
  WorkspaceSetupRequest,
  WorkspaceSetupSignInResult
} from '../shared/workspaceSetup'
import { registerChatFoldersHandlers } from './chatFoldersIpc'
import { runGitCommand } from './gitCommand'
import { type WindowIpcContext } from './ipcBridgeContracts'
import { sanitizeProjectFolderName } from './ipcBridgePaths'
import { registerLinkPreviewHandlers } from './linkPreviews'

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
    const folderName = sanitizeProjectFolderName(typeof params?.name === 'string' ? params.name : '')
    const baseDir = app.getPath('documents')
    let target = path.join(baseDir, folderName)
    for (let suffix = 2; existsSync(target); suffix++) {
      target = path.join(baseDir, `${folderName} ${suffix}`)
    }
    await fs.mkdir(target, { recursive: true })
    let gitInitialized = true
    try {
      await runGitCommand(target, ['init'])
    } catch {
      gitInitialized = false
    }
    return { path: target, gitInitialized }
  })

  handleSafe('workspace:switch', async (_event, newPath: string) => {
    await callbacks.onSwitchWorkspace(newPath)
  })

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

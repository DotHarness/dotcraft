import { normalizeLocale, translate } from '../../shared/locales'
import { stopBeforeArchive } from '../../shared/stopBeforeArchive'
import type { WorkspaceSetupModelListRequest, WorkspaceSetupRequest } from '../../shared/workspaceSetup'
import { ensureDefaultChatWorkspace, isDefaultChatWorkspace } from '../defaultChatWorkspace'
import { DesktopAppServerClient } from '../DesktopAppServerClient'
import { type IpcHandlerCallbacks } from '../ipcBridge'
import { resolveDotCraftRuntimeTools } from '../ripgrepRuntime'
import { forgetLocalProject, saveLocalProjectDetails } from '../settings'
import { viewerBrowserManager } from '../viewerBrowser'
import { invalidateForegroundFileAccess, revokeAllWindowFileAccess } from '../windowFileAccess'
import { getForegroundWorkspaceClient } from '../workspaceConnectionRouting'
import { listSetupModels, loginSetupProvider, runWorkspaceSetup } from '../workspaceSetup'
import { beginWorkspaceThreadListRequest } from '../workspaceThreadCache'
import { startLocalProjectConnection } from './windowSecondaryConnections'
import type { DesktopWindowRuntime } from './windowRuntime'
import { resolveLocalUsageConnection } from './windowUsageStatistics'

export async function removeLocalProject(ctx: DesktopWindowRuntime, workspacePath: string): Promise<void> {
  const entry = ctx.getWorkspaceConnection(workspacePath)
  if (entry?.role === 'secondary') {
    ctx.disposeWorkspaceConnection(entry)
  }
  await ctx.app.localProjects.remove(workspacePath)
  forgetLocalProject(ctx.settings, workspacePath)
  ctx.app.persistWindowSettings(ctx.settings)
  ctx.emitWorkspaceProjects()
}

export async function switchForegroundWorkspace(ctx: DesktopWindowRuntime, newPath: string, connectionKey?: string): Promise<void> {
  if (ctx.mainWindow) invalidateForegroundFileAccess(ctx.mainWindow.webContents.id)
  ctx.deviceOpenGeneration += 1
  if (connectionKey) {
    const entry = ctx.workspaceConnections.get(connectionKey)
    if (!entry?.initializeResult) throw new Error('Workspace connection is not available.')
    await ctx.promoteWorkspaceConnection(entry)
    return
  }
  ctx.clearPendingDeviceOpenRetry()
  if (ctx.activeRemoteProject || ctx.settings.activeRemoteDevice) {
    const previous = ctx.wireClient ? ctx.findWorkspaceConnectionByClient(ctx.wireClient)
      : ctx.activeRemoteProject ? ctx.workspaceConnections.get(ctx.activeRemoteProject.projectId) : undefined
    if (previous) ctx.demoteWorkspaceConnection(previous)
    ctx.setActiveRemoteProject(null)
    await ctx.updateSettings({
      connectionMode: 'local', activeRemoteDevice: undefined,
      activeRemoteStack: undefined, activeRemoteProject: undefined
    })
    ctx.previousLocalForegroundWorkspacePath = null
  }

  if (isDefaultChatWorkspace(newPath)) ensureDefaultChatWorkspace()
  ctx.recordForegroundWorkspace(newPath, true)
  ctx.emitWorkspaceProjects()
  await ctx.connectToAppServer(newPath)
  if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
    const loc = normalizeLocale(ctx.settings.locale)
    ctx.mainWindow.setTitle(
      translate(loc, 'app.titleWithWorkspace', { name: ctx.workspaceTitleName(newPath, loc) })
    )
  }
}

export function buildCallbacks(ctx: DesktopWindowRuntime): IpcHandlerCallbacks {
  return {
    onSwitchWorkspace: ctx.switchForegroundWorkspace,
    onOpenDesktopLink: async (url: string) => {
      const handled = ctx.handleDesktopDeepLink(url)
      if (!handled) throw new Error('Unsupported DotCraft link')
      await handled
    },
    onClearWorkspaceSelection: async () => {
      await ctx.clearWorkspaceSelection()
    },
    onRunWorkspaceSetup: async (request: WorkspaceSetupRequest) => {
      if (!ctx.currentWorkspacePath) {
        throw new Error('Open a workspace before running setup.')
      }
      await runWorkspaceSetup(ctx.currentWorkspacePath, request, ctx.settings)
      ctx.emitCurrentWorkspaceStatus(ctx.currentWorkspacePath)
      await ctx.connectToAppServer(ctx.currentWorkspacePath)
    },
    onListSetupModels: async (request: WorkspaceSetupModelListRequest) => {
      return listSetupModels(request, { settings: ctx.settings })
    },
    onLoginSetupProvider: async (request: WorkspaceSetupModelListRequest) =>
      loginSetupProvider(request, { settings: ctx.settings }),
    onOpenNewWindow: () => {
      ctx.app.openNewWindow()
    },
    onRestartManagedAppServer: ctx.restartCurrentManagedAppServer,
    onRetryAppServerConnection: ctx.retryCurrentAppServerConnection,
    onApplyConnectionSettings: ctx.applyConnectionSettings,
    getSshMachinesManager: ctx.getSshMachinesManager,
    onConnectDockerDeployment: ctx.connectDockerDeployment,
    onDisconnectDockerDeployment: ctx.disconnectDockerDeployment,
    onOpenRemoteProject: ctx.openSshRemoteProject,
    onLeaveRemoteProject: ctx.leaveSshRemoteProject,
    getPairedComputersManager: () => ctx.app.pairedComputers,
    onOpenPairedComputerProject: ctx.openPairedComputerProject,
    onLeavePairedComputerProject: ctx.leavePairedComputerProject,
    onDisconnectRemoteProject: (projectId) => ctx.disconnectRemoteProject(projectId),
    getSettings: () => ctx.settings,
    updateSettings: async (partial) => {
      await ctx.updateSettings(partial)
    },
    getHubClient: () => ctx.app.createHubClient(ctx.settings),
    getAppServerWsConfig: () => (ctx.lastAppServerWsUrl ? { wsUrl: ctx.lastAppServerWsUrl } : null),
    getLocalProjectPaths: () => ctx.app.localProjects.list().map((project) => project.path),
    getWorkspaceProjects: ctx.getWorkspaceProjectsPayload,
    removeProject: async (workspacePath: string) => {
      if (ctx.isWorkspaceForeground(workspacePath)) {
        throw new Error('Cannot remove the foreground project from Projects.')
      }
      await ctx.removeLocalProject(workspacePath)
    },
    saveLocalProject: async (params) => {
      await ctx.app.localProjects.open(params.projectPath?.trim() || params.primaryFolder.trim())
      saveLocalProjectDetails(ctx.settings, params)
      ctx.app.persistWindowSettings(ctx.settings)
      ctx.emitWorkspaceProjects()
    },
    restartWorkspace: async (workspacePath: string) => {
      const hubClient = ctx.app.createHubClient(ctx.settings)
      await hubClient.restartAppServer(workspacePath, resolveDotCraftRuntimeTools())
    },
    stopWorkspace: async (workspacePath: string) => {
      const hubClient = ctx.app.createHubClient(ctx.settings)
      await hubClient.stopAppServer(workspacePath)
    },
    startProject: (workspacePath: string) => startLocalProjectConnection(ctx, workspacePath),
    archiveThreadInWorkspace: async (workspacePath: string, threadId: string, connectionKey?: string) => {
      const id = threadId.trim()
      if (!id) throw new Error('A thread id is required to archive.')
      const entry = connectionKey ? ctx.workspaceConnections.get(connectionKey) : ctx.getWorkspaceConnection(workspacePath)
      if (!entry || !entry.connected) {
        throw new Error('Workspace connection is not available for archiving.')
      }
      await stopBeforeArchive((method, params) => entry.client.sendRequest(method, params), id)
      await ctx.refreshConnectionThreadList(entry)
    },
    getUsageConnection: () => resolveLocalUsageConnection(ctx),
    getConnectionKey: (client) => ctx.findWorkspaceConnectionByClient(client)?.key ?? null,
    getConnectionClient: (key) => {
      const entry = ctx.workspaceConnections.get(key)
      return entry?.connected ? entry.client : null
    },
    getConnectionContext: client => {
      const entry = ctx.findWorkspaceConnectionByClient(client)
      return entry ? { workspacePath: entry.workspacePath, capabilities: entry.initializeResult?.capabilities } : null
    },
    onAppServerRequestStarted: (client, method, params) => {
      const entry = ctx.findWorkspaceConnectionByClient(client)
      return entry ? beginWorkspaceThreadListRequest(entry, method, params) : undefined
    },
    onAppServerRequestCompleted: (client, method, params, result, requestGeneration) => {
      ctx.observeAppServerRequestCompletion(client, method, params, result, requestGeneration)
      ctx.getThreadTasks().observeUserRequest(method, params)
    },
    getUserInputAutoResolutionSnapshot: () => ctx.userInputAutoResolution.getSnapshot(),
    onUserInputConversationPresented: (threadId) => {
      ctx.userInputAutoResolution.setPresentedThread(threadId)
    },
    onUserInputConversationActivity: (threadId) => {
      ctx.userInputAutoResolution.recordConversationActivity(threadId)
    },
    onUserInputAutoResolutionSnoozed: (threadId, requestId) => {
      ctx.userInputAutoResolution.snooze(threadId, requestId)
    },
    getConnectionStatus: () => ctx.lastConnectionStatus,
    getWorkspaceStatus: () => ctx.getWorkspaceStatusForRenderer(ctx.currentWorkspacePath)
  }
}

export function getForegroundWireClient(ctx: DesktopWindowRuntime): DesktopAppServerClient | null {
  return getForegroundWorkspaceClient({
    appQuitting: ctx.runtimeIsStopping(),
    mainWindow: ctx.mainWindow,
    wireClient: ctx.wireClient,
    connection: ctx.workspaceConnections.get(
      ctx.activeRemoteProject?.projectId ?? ctx.localProjectId(ctx.currentWorkspacePath)
    )
  })
}

export function reregisterIpcForWorkspace(ctx: DesktopWindowRuntime, workspacePath: string): void {
  ctx.registerDesktopIpcHandlers(workspacePath, ctx.getForegroundWireClient)
  ctx.oratorio?.contextChanged()
}

export async function openWorkspaceWithoutConnection(
  ctx: DesktopWindowRuntime,
  workspacePath: string
): Promise<void> {
  if (ctx.runtimeIsStopping()) {
    return
  }

  await ctx.teardownRuntime('switch to setup-required workspace')
  ctx.closeActiveRemoteForwards()
  ctx.currentWorkspacePath = workspacePath

  ctx.reregisterIpcForWorkspace(workspacePath)

  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) {
    return
  }

  ctx.emitWorkspaceStatus(win, ctx.getWorkspaceStatusForRenderer(workspacePath))
  ctx.emitConnectionStatus(win, { status: 'disconnected' })
}

export async function clearWorkspaceSelection(ctx: DesktopWindowRuntime): Promise<void> {
  if (ctx.mainWindow) revokeAllWindowFileAccess(ctx.mainWindow.webContents.id)
  if (ctx.currentWorkspacePath) {
    await ctx.teardownRuntime('clear workspace selection', { clearWorkspace: true })
  }
  ctx.closeActiveRemoteForwards()

  if (ctx.mainWindow && !ctx.mainWindow.isDestroyed()) {
    viewerBrowserManager.destroyAllTabs(ctx.mainWindow)
  }

  ctx.currentWorkspacePath = ''

  ctx.settings.lastForegroundEntry = 'welcome'
  ctx.app.persistWindowSettings(ctx.settings)

  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) {
    return
  }

  ctx.reregisterIpcForWorkspace('')
  const loc = normalizeLocale(ctx.settings.locale)
  win.setTitle(translate(loc, 'app.brandSubtitle'))
  ctx.emitWorkspaceStatus(win, ctx.getWorkspaceStatusForRenderer(''))
  ctx.emitConnectionStatus(win, { status: 'disconnected' })
}

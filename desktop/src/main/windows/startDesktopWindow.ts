import { existsSync } from 'node:fs'
import { join } from 'node:path'
import { knownChatsFolderRootsFromSettings } from '../chatFoldersIpc'
import {
  ensureDefaultChatWorkspace,
  isDefaultChatWorkspace,
  resolveDefaultChatWorkspacePath
} from '../defaultChatWorkspace'
import type { DesktopApplicationServices } from '../desktopApplicationServices'
import { threadTaskConnection } from '../desktopRuntimeThreadTools'
import { registerOratorioIpc } from '../oratorio/oratorioIpc'
import { getLocalProjectDetails } from '../settings'
import { registerThreadTasksIpc } from '../threadTasks/threadTasksIpc'
import { bindViewerWindow, resolveScopedViewerFileForAccess, viewerUrlToPath } from '../viewerFileProtocol'
import { viewerBrowserManager } from '../viewerBrowser'
import { fileURLToPath } from 'node:url'
import { readAppServerChatGptAuth } from '../voice/VoiceChatGptTranscriber'
import { registerVoiceIpc } from '../voice/voiceIpc'
import { createWindowIpcMain } from '../windowIpc'
import { hasRemoteEndpointArg, resolveStartupWorkspacePath } from '../workspaceArgs'
import { createWindowRuntime, type DesktopWindowRuntime } from './windowRuntime'

export function startDesktopWindow(
  application: DesktopApplicationServices,
  argv: string[]
): DesktopWindowRuntime {
  const ctx = createWindowRuntime(application, argv)
  let workspace = resolveStartupWorkspacePath(
    ctx.settings,
    argv,
    existsSync,
    resolveDefaultChatWorkspacePath(),
    ctx.resolveConnectionMode(ctx.settings)
  )
  if (workspace && isDefaultChatWorkspace(workspace)) workspace = ensureDefaultChatWorkspace()
  const remote = workspace ? ctx.buildInitialRemoteProject(ctx.settings, workspace) : null
  ctx.setActiveRemoteProject(remote)
  const status = ctx.getWorkspaceStatusForRenderer(workspace)
  const win = ctx.createWindow(workspace, status)
  ctx.mainWindow = win
  ctx.currentWorkspacePath = workspace ?? ''
  viewerBrowserManager.registerPersistenceOwner(win)
  viewerBrowserManager.setRestoreValidator(async (owner, url, scope) => {
    if (!/^(file|dotcraft-viewer):/i.test(url)) return
    const absolutePath = url.startsWith('file:') ? fileURLToPath(url) : viewerUrlToPath(url)
    await resolveScopedViewerFileForAccess(absolutePath, scope.workspacePath, owner.webContents.id)
  })
  ctx.disposers.push(
    bindViewerWindow(win, {
      getWorkspaceRoot: () => ctx.currentWorkspacePath,
      getSecondaryRoots: (root) =>
        application.localProjects.has(root)
          ? (getLocalProjectDetails(ctx.settings, root)?.secondaryFolders ?? [])
          : [],
      getChatFolderRoots: (root) =>
        isDefaultChatWorkspace(root ?? ctx.currentWorkspacePath) ? knownChatsFolderRootsFromSettings(ctx.settings) : []
    })
  )
  const features = createWindowIpcMain(win)
  ctx.disposers.push(() => features.dispose())
  const releaseVoice = registerVoiceIpc(
    {
      webContents: win.webContents,
      captureChatGptAuth: () => {
        const client = ctx.wireClient
        const capabilities = ctx.lastConnectionStatus.capabilities
        return (refresh) => readAppServerChatGptAuth(client, capabilities, refresh)
      },
      isChatGptTranscriptionEnabled: () => ctx.settings.voice?.chatGptTranscription !== false
    },
    features
  )
  ctx.disposers.push(() => {
    void releaseVoice()
  })
  registerThreadTasksIpc(
    {
      tasks: application.tasks,
      getConnection: (threadId) => {
        const entry = threadId
          ? [...ctx.workspaceConnections.values()].find(
              (candidate) =>
                candidate.connected &&
                candidate.threads.some(
                  (value) => value && typeof value === 'object' && (value as { id?: unknown }).id === threadId
                )
            )
          : undefined
        const client = entry?.client ?? ctx.getForegroundWireClient()
        return client
          ? threadTaskConnection(
              client,
              entry?.workspacePath ?? ctx.getProtocolWorkspacePath(),
              ctx.desktopThreadToolOptions()
            )
          : null
      }
    },
    features
  )
  ctx.oratorio = registerOratorioIpc(
    () => ctx.getProtocolWorkspacePath() || null,
    () => application.createHubClient(ctx.settings),
    async () => {
      if (ctx.resolveConnectionMode(ctx.settings) !== 'remote') return null
      const active = ctx.resolveActiveRemoteStack(ctx.settings)
      if (!active) return null
      const [oratorio, appServer] = await Promise.all([
        ctx.getSshMachinesManager().docker.openOratorioTunnel(active.host, active.stack),
        ctx.getSshMachinesManager().docker.openAppServerTunnel(active.host, active.stack)
      ])
      const workspacePath = active.stack.appServerWorkspacePath?.trim() || '/workspace'
      return {
        endpoint: oratorio.endpoint,
        accessToken: oratorio.token,
        workspacePath,
        appServerEndpoint: appServer.wsUrl,
        appServerIdentity: `remote:${active.host.id}:${active.stack.id}:${workspacePath}`
      }
    },
    features,
    win.webContents
  )
  ctx.disposers.push(() => ctx.oratorio?.dispose())
  ctx.registerDesktopIpcHandlers(workspace ?? '', ctx.getForegroundWireClient)
  if (workspace) ctx.recordForegroundWorkspace(workspace, !remote)
  if (
    workspace &&
    status.status === 'ready' &&
    existsSync(join(workspace, '.craft', 'config.json')) &&
    ctx.resolveConnectionMode(ctx.settings) !== 'remote' &&
    !hasRemoteEndpointArg(argv)
  )
    ctx.prewarmLocalAppServer(workspace)
  win.on('focus', application.refreshAppMenu)
  win.webContents.once('did-finish-load', () => {
    ctx.emitWorkspaceStatus(win, status)
    ctx.getSshMachinesManager().connectAutoMachines()
    if (workspace && status.status === 'ready')
      void ctx.connectWorkspaceForLoadedWindow(win, workspace).then(ctx.flushPendingNewThreadLink)
    else {
      ctx.emitConnectionStatus(win, { status: 'disconnected' })
      ctx.flushPendingNewThreadLink()
    }
    if (ctx.pendingChromeSettingsDeepLink) ctx.openChromeSettingsFromDeepLink()
  })
  if (import.meta.env.DEV) {
    void win.loadURL('http://localhost:5173')
    win.webContents.once('did-finish-load', () => win.webContents.openDevTools())
  } else void win.loadFile(join(__dirname, '../renderer/index.html'))
  return ctx
}

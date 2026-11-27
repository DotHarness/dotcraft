import { BrowserWindow } from 'electron'
import {
  DESKTOP_TOOL_CONFIRM_DISMISSED_CHANNEL,
  DESKTOP_TOOL_CONFIRM_REQUEST_CHANNEL,
  type DesktopToolConfirmDetails,
  type DesktopToolConfirmOutcome
} from '../../shared/desktopToolConfirm'
import { type ThreadTask } from '../../shared/threadTasks'
import { createChatFolderFromSettings } from '../chatFoldersIpc'
import { isDefaultChatWorkspace } from '../defaultChatWorkspace'
import { computerUseManager } from '../computerUse/runtime'
import {
  handleDesktopRuntimeThreadToolCall,
  hasDesktopThreadTools,
  resetDesktopThreadToolBindings,
  threadTaskConnection,
  type DesktopAppServerRequestOptions
} from '../desktopRuntimeThreadTools'
import { broadcastServerRequest, createServerRequestBridge } from '../ipcBridge'
import { openDevServerPreview } from '../devServers/openDevServerPreview'
import {
  createNewLocalProjectFolder,
  planNewLocalProjectFolder,
  registerLocalProject
} from '../localProjectOperations'
import { getLocalProjectDetails } from '../settings'
import { themeHostFor } from '../themes/themeIpc'
import type { DesktopThreadTasks } from '../threadTasks/threadTaskService'
import { viewerTerminalManager } from '../viewerTerminal'
import {
  NEW_TASK_OFFER_ATTENTION,
  DESKTOP_TOOL_CONFIRM_ATTENTION,
  WRAP_UP_QUESTION_ATTENTION
} from '../windowAttentionNotifier'
import { hasRemoteEndpointArg } from '../workspaceArgs'
import { isRendererInteractiveServerRequest } from '../workspaceConnectionRouting'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'
import {
  disconnectedThreadProjectName,
  holdThreadProjectRequest,
  listThreadProjects,
  openThreadProject,
  threadProjectForEntry
} from './windowThreadProjects'
import {
  hasPendingWorkspaceComputerUseApproval,
  isCurrentWorkspaceRequestSource
} from './windowNodeReplMcp'

export function desktopThreadToolOptions(ctx: DesktopWindowRuntime, source?: WorkspaceConnectionEntry): DesktopAppServerRequestOptions {
  const entry = source ?? [...ctx.workspaceConnections.values()].find(candidate => candidate.client === ctx.wireClient)
  return {
    ...(entry ? {
      project: threadProjectForEntry(ctx, entry),
      remote: entry.kind === 'remote',
      holdThreadRequest: <T>(request: () => Promise<T>) => holdThreadProjectRequest(entry, request),
      projects: {
        list: () => listThreadProjects(ctx),
        disconnected: id => disconnectedThreadProjectName(ctx, id),
        connect: async project => {
          const target = await openThreadProject(ctx, project)
          return threadTaskConnection(target.client, target.workspacePath, desktopThreadToolOptions(ctx, target))
        }
      }
    } : {}),
    supportsDynamicToolRebind: (source ? source.initializeResult?.capabilities : ctx.lastConnectionStatus.capabilities)?.dynamicToolRebind === true,
    projectWorkspaceFor: (client, workspacePath) => {
      const connection = [...ctx.workspaceConnections.values()].find((entry) => entry.client === client)
      if (connection?.kind !== 'local' || !ctx.app.localProjects.has(workspacePath)) return undefined
      const details = getLocalProjectDetails(ctx.settings, workspacePath)
      const cwd = details?.primaryFolder ?? workspacePath
      return { cwd, runtimeWorkspaceRoots: [cwd, ...(details?.secondaryFolders ?? [])] }
    },
    ...((source ? source.kind !== 'remote' && isDefaultChatWorkspace(source.workspacePath) : ctx.isLocalChatsForeground())
      ? {
          createChatFolder: (firstMessage: string) => createChatFolderFromSettings(ctx.settings, firstMessage)
        }
      : {}),
    settingsHost: {
      getSettings: () => ctx.settings,
      updateSettings: ctx.updateSettings,
      onPinnedThreadIdsChanged: (workspacePath, threadIds) => {
        ctx.app.broadcastPinnedThreadIdsChanged({ workspacePath, threadIds })
      }
    },
    tasks: ctx.getThreadTasks(),
    confirmTool: (details, threadId, turnId) => confirmDesktopTool(ctx, details, threadId, turnId),
    ownerId: ctx.mainWindow?.webContents.id,
    themes: ctx.mainWindow ? themeHostFor(ctx.mainWindow) : undefined,
    ...(entry?.kind === 'local' ? {
      openDevServerPreview: preview => {
        if (ctx.mainWindow && isCurrentWorkspaceRequestSource(ctx, entry)) {
          openDevServerPreview(ctx.mainWindow, entry.projectId, preview)
        }
      }
    } : {}),
    ...(entry && entry.kind !== 'remote' && !hasRemoteEndpointArg(ctx.argv) ? {
      projectCreator: {
        refreshProjects: async () => {
          await ctx.app.localProjects.refresh()
        },
        existingProjectId: (projectPath: string) =>
          ctx.app.localProjects.has(projectPath) ? ctx.localProjectId(projectPath) : undefined,
        planNewFolder: (name: string) => planNewLocalProjectFolder(name),
        createNewFolder: (name: string, preferredPath: string) =>
          createNewLocalProjectFolder(name, { preferredPath }),
        confirmCreation: ({ threadId, turnId, ...project }) =>
          confirmDesktopTool(ctx, { kind: 'projectCreation', ...project }, threadId, turnId),
        registerProject: (params) =>
          registerLocalProject({
            openInHub: (projectPath) => ctx.app.localProjects.open(projectPath),
            settings: ctx.settings,
            persistSettings: ctx.app.persistWindowSettings,
            broadcastProjects: ctx.app.broadcastWorkspaceProjects
          }, params)
      }
    } : {}),
    terminals: {
      threadTabs: (threadId) =>
        ctx.mainWindow && !ctx.mainWindow.isDestroyed()
          ? viewerTerminalManager.threadTabs(ctx.mainWindow, threadId)
          : [],
      waitForOutput: (tabId, timeoutMs) =>
        ctx.mainWindow && !ctx.mainWindow.isDestroyed()
          ? viewerTerminalManager.waitForOutput(ctx.mainWindow, tabId, timeoutMs)
          : Promise.resolve('gone')
    }
  }
}

function confirmDesktopTool(
  ctx: DesktopWindowRuntime,
  details: DesktopToolConfirmDetails,
  threadId: string | undefined,
  turnId: string | undefined
): Promise<DesktopToolConfirmOutcome> {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) return Promise.resolve('abandoned')
  return ctx.app.toolConfirm.confirm({
    ownerId: win.webContents.id,
    threadId,
    turnId,
    details,
    sendToRenderer: (payload) => {
      if (win.isDestroyed()) return false
      win.webContents.send(DESKTOP_TOOL_CONFIRM_REQUEST_CHANNEL, payload)
      return true
    },
    dismissInRenderer: (requestId) => {
      if (!win.isDestroyed()) {
        win.webContents.send(DESKTOP_TOOL_CONFIRM_DISMISSED_CHANNEL, { requestId })
      }
    },
    notifyAttention: (requestId) => {
      if (threadId) notifyThreadAttention(ctx, DESKTOP_TOOL_CONFIRM_ATTENTION, threadId, turnId, requestId)
    }
  })
}

export function getThreadTasks(ctx: DesktopWindowRuntime): DesktopThreadTasks {
  return ctx.app.tasks
}

export function notifyNewTaskOffer(ctx: DesktopWindowRuntime, task: ThreadTask): void {
  notifyThreadAttention(ctx, NEW_TASK_OFFER_ATTENTION, task.threadId, task.turnId, task.id)
}

export function notifyWrapUpQuestion(ctx: DesktopWindowRuntime, threadId: string, turnId: string | undefined, callId: string): void {
  notifyThreadAttention(ctx, WRAP_UP_QUESTION_ATTENTION, threadId, turnId, callId)
}

function notifyThreadAttention(ctx: DesktopWindowRuntime, kind: string, threadId: string, turnId: string | undefined, requestId: string): void {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) return
  const entry = [...ctx.workspaceConnections.values()].find(candidate => candidate.connected
    && candidate.threads.some(value => value && typeof value === 'object' && (value as { id?: string }).id === threadId))
  if (!entry) return
  void ctx.app
    .notifyWindowAttention(
      kind,
      { threadId, turnId, requestId },
      {
        connectionKind: entry.kind,
        settings: ctx.settings,
        threads: entry.threads,
        isViewingThread: (threadId) => ctx.userInputAutoResolution.isViewing(threadId),
        openThread: (threadId) => openSourceThread(ctx, entry, win, threadId)
      }
    )
    .catch(console.warn)
}

export function openSourceThread(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry, win: BrowserWindow, threadId: string): void {
  void (async () => {
    if (!isCurrentWorkspaceRequestSource(ctx, entry)) return
    if (entry.role !== 'foreground' || ctx.wireClient !== entry.client) {
      if (await ctx.promoteWorkspaceConnection(entry) === false) return
    }
    ctx.showWindowSafely(win)
    ctx.sendOpenThread(win, threadId)
  })().catch(console.warn)
}

export function resetThreadToolState(ctx: DesktopWindowRuntime): void {
  if (ctx.wireClient) resetDesktopThreadToolBindings(ctx.wireClient)
  if (ctx.mainWindow) {
    ctx.getThreadTasks().abandonOffersForOwner(ctx.mainWindow.webContents.id)
    ctx.app.toolConfirm.abandonOwner(ctx.mainWindow.webContents.id)
  }
}

export async function handleServerRequestInMain(
  ctx: DesktopWindowRuntime,
  method: string,
  params: unknown,
  source: WorkspaceConnectionEntry
): Promise<unknown | undefined> {
  if (!ctx.mainWindow || ctx.mainWindow.isDestroyed()) {
    throw new Error('Window is not available to handle server request')
  }

  if (method === 'item/tool/call') {
    return handleDesktopRuntimeThreadToolCall(
      source.client,
      params,
      source.workspacePath,
      ctx.desktopThreadToolOptions(source)
    )
  }

  return undefined
}

export function observeComputerUseApproval(
  ctx: DesktopWindowRuntime,
  win: BrowserWindow,
  params: unknown,
  _response: Promise<unknown>,
  foreground: boolean
): void {
  const request = (params ?? {}) as Record<string, unknown>
  if (
    foreground &&
    typeof request.threadId === 'string' &&
    typeof request.turnId === 'string' &&
    computerUseManager.hasActiveTurn(request.threadId, request.turnId)
  ) {
    ctx.showWindowSafely(win)
    ctx.sendOpenThread(win, request.threadId)
  }
}

function isTrustedComputerUseElicitation(
  entry: WorkspaceConnectionEntry,
  method: string,
  params: unknown
): boolean {
  if (method !== 'mcpServer/elicitation/request' || entry.kind === 'remote' || !params || typeof params !== 'object') {
    return false
  }
  const request = params as Record<string, unknown>
  const meta = request._meta != null && typeof request._meta === 'object' && !Array.isArray(request._meta)
    ? request._meta as Record<string, unknown>
    : undefined
  return request.serverName === 'node_repl'
    && meta?.approvalKind === 'computerUse'
    && typeof request.threadId === 'string'
    && hasPendingWorkspaceComputerUseApproval(entry, request.threadId)
}

export async function bridgeServerRequestToRenderer(
  ctx: DesktopWindowRuntime,
  method: string,
  params: unknown,
  entry: WorkspaceConnectionEntry,
  requestId: string | number,
  signal: AbortSignal
): Promise<unknown> {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) {
    throw new Error('Window is not available to handle server request')
  }
  const connectionId = entry.client.connectionId
  if (!connectionId) throw new Error('Workspace connection is no longer available.')
  const source = { connectionKey: entry.key, connectionId, wireRequestId: requestId }
  let bridgeId = ''
  const bridge = createServerRequestBridge(() => ctx.userInputAutoResolution.remove(bridgeId), win, source, signal)
  bridgeId = bridge.bridgeId
  const { promise } = bridge
  if (signal.aborted) return promise
  if (method === 'item/tool/requestUserInput') {
    const request = (params ?? {}) as Record<string, unknown>
    if (
      typeof request.threadId === 'string' &&
      typeof request.requestId === 'string' &&
      typeof request.isBlocking === 'boolean'
    ) {
      ctx.userInputAutoResolution.track({
        bridgeId,
        threadId: request.threadId,
        requestId: request.requestId,
        isBlocking: request.isBlocking
      })
    }
  }
  const trustedComputerUse = isTrustedComputerUseElicitation(entry, method, params)
  broadcastServerRequest(win, {
    bridgeId,
    method,
    params,
    ...source,
    ...(trustedComputerUse ? { trustedComputerUse: true } : {})
  })
  void ctx.app
    .notifyWindowAttention(method, params, {
      connectionKind: entry.kind,
      settings: ctx.settings,
      threads: entry.threads,
      isViewingThread: (threadId) => ctx.userInputAutoResolution.isViewing(threadId),
      openThread: (threadId) => openSourceThread(ctx, entry, win, threadId)
    })
    .catch(console.warn)
  if (method === 'item/approval/request' || method === 'mcpServer/elicitation/request') {
    ctx.observeComputerUseApproval(win, params, promise, entry.role === 'foreground' && ctx.wireClient === entry.client)
  }
  return promise
}

export async function handleWorkspaceServerRequest(
  ctx: DesktopWindowRuntime,
  method: string,
  params: unknown,
  entry: WorkspaceConnectionEntry,
  canUseForegroundMainHandlers: boolean,
  canBridgeInteractiveToRenderer: boolean,
  requestId: string | number,
  signal: AbortSignal
): Promise<unknown> {
  if (!isCurrentWorkspaceRequestSource(ctx, entry)) {
    throw new Error('Workspace request source is no longer active.')
  }
  canUseForegroundMainHandlers = canUseForegroundMainHandlers && entry.role === 'foreground' && ctx.wireClient === entry.client
  const isInteractive = isRendererInteractiveServerRequest(method)
  const threadId = (params as { threadId?: string } | null)?.threadId
  const registeredTool = method === 'item/tool/call' && typeof threadId === 'string'
    && hasDesktopThreadTools(entry.client, threadId, ctx.desktopThreadToolOptions(entry))

  if (!canUseForegroundMainHandlers && !isInteractive && !registeredTool) {
    throw new Error(`Server request ${method} is not routed to the current foreground workspace connection.`)
  }

  if (canUseForegroundMainHandlers || registeredTool) {
    const handledInMain = await ctx.handleServerRequestInMain(method, params, entry)
    if (handledInMain !== undefined) return handledInMain
  }

  if (!canUseForegroundMainHandlers && !canBridgeInteractiveToRenderer) {
    throw new Error('Window is not available to handle interactive server request.')
  }

  return ctx.bridgeServerRequestToRenderer(method, params, entry, requestId, signal)
}

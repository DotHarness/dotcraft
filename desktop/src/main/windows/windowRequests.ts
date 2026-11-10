import { BrowserWindow } from 'electron'
import { type ThreadTask } from '../../shared/threadTasks'
import { createChatFolderFromSettings } from '../chatFoldersIpc'
import { isDefaultChatWorkspace } from '../defaultChatWorkspace'
import { computerUseManager, rememberAlwaysAllowedApp } from '../computerUse/runtime'
import {
  handleDesktopRuntimeThreadToolCall,
  hasDesktopThreadTools,
  resetDesktopThreadToolBindings,
  type DesktopAppServerRequestOptions
} from '../desktopRuntimeThreadTools'
import { broadcastServerRequest, createServerRequestBridge } from '../ipcBridge'
import { getLocalProjectDetails } from '../settings'
import type { DesktopThreadTasks } from '../threadTasks/threadTaskService'
import { viewerTerminalManager } from '../viewerTerminal'
import { NEW_TASK_OFFER_ATTENTION } from '../windowAttentionNotifier'
import { isRendererInteractiveServerRequest } from '../workspaceConnectionRouting'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'
import { handleWorkspaceNodeReplRequest, isCurrentWorkspaceRequestSource, isWorkspaceNodeReplRequest } from './windowNodeReplRequests'

export function desktopThreadToolOptions(ctx: DesktopWindowRuntime, source?: WorkspaceConnectionEntry): DesktopAppServerRequestOptions {
  return {
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
    ownerId: ctx.mainWindow?.webContents.id,
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

export function getThreadTasks(ctx: DesktopWindowRuntime): DesktopThreadTasks {
  return ctx.app.tasks
}

export function notifyNewTaskOffer(ctx: DesktopWindowRuntime, task: ThreadTask): void {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) return
  const entry = [...ctx.workspaceConnections.values()].find(candidate => candidate.connected
    && candidate.threads.some(value => value && typeof value === 'object' && (value as { id?: string }).id === task.threadId))
  if (!entry) return
  void ctx.app
    .notifyWindowAttention(
      NEW_TASK_OFFER_ATTENTION,
      { threadId: task.threadId, turnId: task.turnId, requestId: task.id },
      {
        connectionKind: entry.kind,
        window: win,
        settings: ctx.settings,
        threads: entry.threads,
        openThread: (threadId) => openSourceThread(ctx, entry, win, threadId)
      }
    )
    .catch(console.warn)
}

function openSourceThread(ctx: DesktopWindowRuntime, entry: WorkspaceConnectionEntry, win: BrowserWindow, threadId: string): void {
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
  if (ctx.mainWindow) ctx.getThreadTasks().abandonOffersForOwner(ctx.mainWindow.webContents.id)
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
  response: Promise<unknown>,
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
  if (request.approvalType !== 'computerUse' || typeof request.target !== 'string') return
  const target = request.target
  const displayName =
    typeof request.targetLabel === 'string' && request.targetLabel ? request.targetLabel : target
  void response
    .then((result) => {
      if ((result as { decision?: unknown } | undefined)?.decision === 'acceptAlways') {
        return rememberAlwaysAllowedApp({ id: target, displayName })
      }
    })
    .catch(() => {})
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
  broadcastServerRequest(win, { bridgeId, method, params, ...source })
  void ctx.app
    .notifyWindowAttention(method, params, {
      connectionKind: entry.kind,
      window: win,
      settings: ctx.settings,
      threads: entry.threads,
      openThread: (threadId) => openSourceThread(ctx, entry, win, threadId)
    })
    .catch(console.warn)
  if (method === 'item/approval/request') {
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
  if (isWorkspaceNodeReplRequest(method)) return handleWorkspaceNodeReplRequest(ctx, entry, method, params)
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

import { BrowserWindow } from 'electron'
import { type ThreadTask } from '../../shared/threadTasks'
import { createChatFolderFromSettings } from '../chatFoldersIpc'
import { computerUseManager, rememberAlwaysAllowedApp } from '../computerUse/runtime'
import {
  handleDesktopRuntimeThreadToolCall,
  resetDesktopThreadToolBindings,
  type DesktopAppServerRequestOptions
} from '../desktopRuntimeThreadTools'
import { broadcastServerRequest, createServerRequestBridge } from '../ipcBridge'
import type { DesktopThreadTasks } from '../threadTasks/threadTaskService'
import { viewerTerminalManager } from '../viewerTerminal'
import { NEW_TASK_OFFER_ATTENTION } from '../windowAttentionNotifier'
import { isRendererInteractiveServerRequest } from '../workspaceConnectionRouting'
import type { DesktopWindowRuntime } from './windowRuntime'
import type { WorkspaceConnectionEntry } from './windowTypes'
import { handleWorkspaceNodeReplRequest, isCurrentWorkspaceRequestSource, isWorkspaceNodeReplRequest } from './windowNodeReplRequests'

export function desktopThreadToolOptions(ctx: DesktopWindowRuntime): DesktopAppServerRequestOptions {
  return {
    supportsDynamicToolRebind: ctx.lastConnectionStatus.capabilities?.dynamicToolRebind === true,
    ...(ctx.isLocalChatsForeground()
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
  const entry = [...ctx.workspaceConnections.values()].find(
    (candidate) => candidate.client === ctx.wireClient
  )
  void ctx.app
    .notifyWindowAttention(
      NEW_TASK_OFFER_ATTENTION,
      { threadId: task.threadId, turnId: task.turnId, requestId: task.id },
      {
        connectionKind: entry?.kind ?? 'local',
        window: win,
        settings: ctx.settings,
        threads: entry?.threads ?? [],
        openThread: (threadId) => {
          ctx.showWindowSafely(win)
          ctx.sendOpenThread(win, threadId)
        }
      }
    )
    .catch(console.warn)
}

export function resetThreadToolState(ctx: DesktopWindowRuntime): void {
  if (ctx.wireClient) resetDesktopThreadToolBindings(ctx.wireClient)
  if (ctx.mainWindow) ctx.getThreadTasks().abandonOffersForOwner(ctx.mainWindow.webContents.id)
}

export async function handleServerRequestInMain(
  ctx: DesktopWindowRuntime,
  method: string,
  params: unknown
): Promise<unknown | undefined> {
  if (!ctx.mainWindow || ctx.mainWindow.isDestroyed()) {
    throw new Error('Window is not available to handle server request')
  }

  if (method === 'item/tool/call') {
    const client = ctx.wireClient
    if (!client) {
      return undefined
    }
    return handleDesktopRuntimeThreadToolCall(
      client,
      params,
      ctx.getProtocolWorkspacePath(),
      ctx.desktopThreadToolOptions()
    )
  }

  return undefined
}

export function observeComputerUseApproval(
  ctx: DesktopWindowRuntime,
  win: BrowserWindow,
  params: unknown,
  response: Promise<unknown>
): void {
  const request = (params ?? {}) as Record<string, unknown>
  if (
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
  entry: WorkspaceConnectionEntry
): Promise<unknown> {
  const win = ctx.mainWindow
  if (!win || win.isDestroyed()) {
    throw new Error('Window is not available to handle server request')
  }
  let bridgeId = ''
  const bridge = createServerRequestBridge(() => ctx.userInputAutoResolution.remove(bridgeId), win)
  bridgeId = bridge.bridgeId
  const { promise } = bridge
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
  broadcastServerRequest(win, { bridgeId, method, params })
  void ctx.app
    .notifyWindowAttention(method, params, {
      connectionKind: entry.kind,
      window: win,
      settings: ctx.settings,
      threads: entry.threads,
      openThread: (threadId) => {
        ctx.showWindowSafely(win)
        ctx.sendOpenThread(win, threadId)
      }
    })
    .catch(console.warn)
  if (method === 'item/approval/request') ctx.observeComputerUseApproval(win, params, promise)
  return promise
}

export async function handleWorkspaceServerRequest(
  ctx: DesktopWindowRuntime,
  method: string,
  params: unknown,
  entry: WorkspaceConnectionEntry,
  canUseForegroundMainHandlers: boolean,
  canBridgeInteractiveToRenderer: boolean
): Promise<unknown> {
  if (!isCurrentWorkspaceRequestSource(ctx, entry)) {
    throw new Error('Workspace request source is no longer active.')
  }
  if (isWorkspaceNodeReplRequest(method)) return handleWorkspaceNodeReplRequest(ctx, entry, method, params)
  canUseForegroundMainHandlers = canUseForegroundMainHandlers && entry.role === 'foreground' && ctx.wireClient === entry.client
  const isInteractive = isRendererInteractiveServerRequest(method)

  if (!canUseForegroundMainHandlers && !isInteractive) {
    throw new Error(`Server request ${method} is not routed to the current foreground workspace connection.`)
  }

  if (canUseForegroundMainHandlers) {
    const handledInMain = await ctx.handleServerRequestInMain(method, params)
    if (handledInMain !== undefined) return handledInMain
  }

  if (!canUseForegroundMainHandlers && !canBridgeInteractiveToRenderer) {
    throw new Error('Window is not available to handle interactive server request.')
  }

  return ctx.bridgeServerRequestToRenderer(method, params, entry)
}

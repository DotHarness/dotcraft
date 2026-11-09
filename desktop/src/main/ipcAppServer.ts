import { dialog, type OpenDialogOptions } from 'electron'
import type { AppServerIpcRequest } from '../shared/appServerBoundary'
import { toAppServerErrorEnvelope } from '../shared/appServerError'
import type { RetryConnectionRequest } from '../shared/connectionStatus'
import { translate } from '../shared/locales'
import { type ConnectionSettingsDraft } from '../shared/remoteConnection'
import { resolveBinaryLocation } from './AppServerManager'
import type { DesktopAppServerClient } from './DesktopAppServerClient'
import { hasDesktopThreadTools, sendDesktopAppServerRequest } from './desktopRuntimeThreadTools'
import type { ResolvedBinaryRequest } from './ipcBridgeContracts'
import { mainLocale, type WindowIpcContext } from './ipcBridgeContracts'
import { cancelServerRequestBridge, resolveServerRequestBridge } from './ipcServerRequests'
export function withDesktopRequestIdentity(
  method: string,
  params: unknown,
  workspacePath: string | null | undefined
): unknown {
  if (
    method !== 'thread/start' ||
    params == null ||
    typeof params !== 'object' ||
    Array.isArray(params) ||
    (params as Record<string, unknown>).identity != null
  ) {
    return params
  }
  if (!workspacePath) throw new Error('Desktop thread/start requires an active workspace.')
  return {
    ...(params as Record<string, unknown>),
    identity: {
      channelName: 'dotcraft-desktop',
      userId: 'local',
      channelContext: `workspace:${workspacePath}`,
      workspacePath
    }
  }
}

export function registerAppServerHandlers(context: WindowIpcContext): void {
  const { handleSafe, callbacks, getWireClient, owner } = context
  const withoutTokenRequest = (params: unknown): Record<string, unknown> => {
    if (params == null || typeof params !== 'object') return {}
    const { includeToken: _includeToken, refreshToken: _refreshToken, ...rest } = params as Record<string, unknown>
    return rest
  }

  // Failures answer with an envelope rather than throwing: Electron would otherwise
  // flatten the JSON-RPC code and `data` to a message.
  const sendAppServerRequest = async (
    method: string,
    params: unknown,
    timeoutMs: number | null | undefined,
    onCompleted?: (
      client: DesktopAppServerClient,
      requestParams: unknown,
      result: unknown,
      requestGeneration?: number
    ) => void,
    onStarted?: (client: DesktopAppServerClient, requestParams: unknown) => number | undefined,
    resolveClient: () => DesktopAppServerClient | null = getWireClient,
    attachmentOwner?: string,
    deliver?: (result: unknown) => void,
    expectedConnectionId?: string
  ): Promise<void> => {
    let delivered = false
    const respond = (result: unknown): void => {
      if (delivered) return
      delivered = true
      deliver?.(result)
    }
    try {
      const client = resolveClient()
      if (!client) {
        throw new Error(`${translate(mainLocale(callbacks), 'ipc.appServerNotConnected')} (${method})`)
      }
      if (expectedConnectionId !== undefined && client.connectionId !== expectedConnectionId) {
        throw new Error('The AppServer connection is no longer active.')
      }
      const connection = callbacks.getConnectionContext(client)
      if (!connection) throw new Error('The AppServer connection is no longer active.')
      const requestParams = withDesktopRequestIdentity(
        method,
        method === 'provider/auth/status' ? withoutTokenRequest(params) : (params ?? {}),
        connection.workspacePath
      )
      const requestGeneration = onStarted?.(client, requestParams)
      await sendDesktopAppServerRequest(client, method, requestParams, timeoutMs, {
        ownerId: owner.webContents.id,
        attachmentOwner,
        onError: error => respond(toAppServerErrorEnvelope(error)),
        onResponse: result => {
          onCompleted?.(client, requestParams, result, requestGeneration)
          respond(result)
        },
        supportsDynamicToolRebind: connection.capabilities?.dynamicToolRebind === true
      })
    } catch (error) {
      respond(toAppServerErrorEnvelope(error))
    }
  }

  context.ipc.on('appserver:request', (_event, request: AppServerIpcRequest) => {
    void sendAppServerRequest(
      request.method, request.params, request.timeoutMs,
      (client, requestParams, result, requestGeneration) => {
        callbacks.onAppServerRequestCompleted?.(client, request.method, requestParams, result, requestGeneration)
      },
      (client, requestParams) => callbacks.onAppServerRequestStarted?.(client, request.method, requestParams),
      request.connectionKey === undefined ? getWireClient : () => callbacks.getConnectionClient?.(request.connectionKey!) ?? null,
      request.attachmentOwner,
      result => {
        if (!owner.isDestroyed() && !owner.webContents.isDestroyed()) {
          owner.webContents.send('appserver:delivery', { kind: 'response', requestId: request.requestId, result })
        }
      },
      request.connectionId
    )
  })

  handleSafe('appserver:get-usage-connection', () => callbacks.getUsageConnection())

  handleSafe('appserver:get-connection-key', () => {
    const client = getWireClient()
    return client ? (callbacks.getConnectionKey?.(client) ?? null) : null
  })

  handleSafe('appserver:model-list', async (_event, providerId?: string | null, refresh = false) => {
    const client = getWireClient()
    if (!client) {
      return null
    }
    const normalizedProviderId = providerId?.trim() || null
    return client.sendRequest(
      'model/list',
      { ...(normalizedProviderId ? { providerId: normalizedProviderId } : {}), ...(refresh ? { refresh: true } : {}) },
      20_000
    )
  })

  handleSafe('appserver:get-connection-status', () => {
    return callbacks.getConnectionStatus()
  })

  handleSafe('appserver:has-desktop-thread-tools', (_event, threadId: string) => {
    const client = getWireClient()
    return (
      client !== null &&
      hasDesktopThreadTools(client, threadId, {
        ownerId: owner.webContents.id,
        supportsDynamicToolRebind: callbacks.getConnectionStatus().capabilities?.dynamicToolRebind === true
      })
    )
  })

  handleSafe('appserver:resolved-binary', (_event, request?: ResolvedBinaryRequest) => {
    const settings = callbacks.getSettings()
    return resolveBinaryLocation({
      binarySource: request?.binarySource ?? settings.binarySource,
      binaryPath: request?.binaryPath ?? settings.appServerBinaryPath,
      preferDevBuild: import.meta.env.DEV,
      requireDevBuild: import.meta.env.DEV
    })
  })

  handleSafe('appserver:pick-binary', async (_event) => {
    const options: OpenDialogOptions =
      process.platform === 'win32'
        ? {
            title: translate(mainLocale(callbacks), 'settings.pickBinaryTitle'),
            properties: ['openFile'],
            filters: [{ name: 'DotCraft', extensions: ['exe'] }]
          }
        : {
            title: translate(mainLocale(callbacks), 'settings.pickBinaryTitle'),
            properties: ['openFile']
          }
    const result = await dialog.showOpenDialog(owner, options)
    if (result.canceled || result.filePaths.length === 0) return null
    return result.filePaths[0]
  })

  handleSafe('appserver:restart-managed', async () => {
    await callbacks.onRestartManagedAppServer()
  })

  handleSafe('appserver:retry-connection', async (_event, request?: RetryConnectionRequest) => {
    if (!callbacks.onRetryAppServerConnection) {
      throw new Error('AppServer connection retry is not available right now.')
    }
    await callbacks.onRetryAppServerConnection(request)
  })

  handleSafe('appserver:apply-connection-settings', async (_event, draft: ConnectionSettingsDraft) => {
    if (!callbacks.onApplyConnectionSettings) {
      throw new Error('Connection settings cannot be applied right now.')
    }
    await callbacks.onApplyConnectionSettings(draft)
  })

  handleSafe('appserver:server-response', (_event, bridgeId: string, result: unknown) => {
    resolveServerRequestBridge(bridgeId, result, owner.webContents.id)
  })

  handleSafe('appserver:cancel-server-request', (_event, bridgeId: string) => {
    cancelServerRequestBridge(bridgeId, owner.webContents.id)
  })

  handleSafe(
    'appserver:user-input-auto-resolution-snapshot',
    () => callbacks.getUserInputAutoResolutionSnapshot?.() ?? []
  )

  handleSafe('appserver:user-input-conversation-presented', (_event, threadId: string | null) => {
    callbacks.onUserInputConversationPresented?.(threadId)
  })

  handleSafe('appserver:user-input-conversation-activity', (_event, threadId: string) => {
    callbacks.onUserInputConversationActivity?.(threadId)
  })

  handleSafe('appserver:user-input-auto-resolution-snooze', (_event, threadId: string, requestId: string) => {
    callbacks.onUserInputAutoResolutionSnoozed?.(threadId, requestId)
  })
}

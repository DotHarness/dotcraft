import type { AppListResult } from '@dotcraft/sdk/contracts'
import { translate } from '../shared/locales'
import { registerDesktopPluginModuleIpc } from './desktopPluginModuleIpc'
import { sendDesktopAppServerRequest } from './desktopRuntimeThreadTools'
import { requestDesktopPluginAppSurfaceJson } from './ipcAppSurfaces'
import { mainLocale, type WindowIpcContext } from './ipcBridgeContracts'
import { openAppHandoffUrl } from './ipcExternalLinks'

export function registerDesktopAppHandlers(context: WindowIpcContext): void {
  const { handleSafe, workspacePath, callbacks, getWireClient, ipc, owner } = context
  const sendDesktopPluginAppBindingRequest = async (
    appId: string,
    method: 'app/connection/status' | 'app/connection/start'
  ): Promise<unknown> => {
    if (typeof appId !== 'string' || !appId.trim()) throw new Error('App id is required.')
    const client = getWireClient()
    if (!client) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
    }
    return sendDesktopAppServerRequest(client, method, { appId }, 20_000, {
      ownerId: owner.webContents.id,
      supportsDynamicToolRebind: callbacks.getConnectionStatus().capabilities?.dynamicToolRebind === true
    })
  }

  registerDesktopPluginModuleIpc(getWireClient, callbacks, workspacePath, ipc, owner.webContents.id)

  const requestDesktopPluginAppSurface = async (
    params: {
      appId: string
      surfaceId: string
      relativePath: string
      body?: unknown
      timeoutMs?: number
    },
    method: 'GET' | 'POST'
  ): Promise<unknown> => {
    const client = getWireClient()
    if (!client) {
      throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
    }
    return requestDesktopPluginAppSurfaceJson(
      client,
      method,
      params.appId,
      params.surfaceId,
      params.relativePath,
      params.body,
      params.timeoutMs
    )
  }

  handleSafe(
    'desktop-plugin:app-surface-get-json',
    async (
      _event,
      params: {
        appId: string
        surfaceId: string
        relativePath: string
        timeoutMs?: number
      }
    ) => requestDesktopPluginAppSurface(params, 'GET')
  )

  handleSafe(
    'desktop-plugin:app-surface-post-json',
    async (
      _event,
      params: {
        appId: string
        surfaceId: string
        relativePath: string
        body?: unknown
        timeoutMs?: number
      }
    ) => requestDesktopPluginAppSurface(params, 'POST')
  )

  handleSafe('desktop-plugin:app-connection-status', async (_event, params: { appId: string }) => {
    return sendDesktopPluginAppBindingRequest(params.appId, 'app/connection/status')
  })

  handleSafe('desktop-plugin:app-connection-start', async (_event, params: { appId: string }) => {
    return sendDesktopPluginAppBindingRequest(params.appId, 'app/connection/start')
  })

  handleSafe('desktop-plugin:app-open', async (_event, params: { appId: string; url: string }) => {
    const client = getWireClient()
    if (!client) throw new Error(translate(mainLocale(callbacks), 'ipc.appServerNotConnected'))
    const result = await sendDesktopAppServerRequest<AppListResult>(
      client,
      'app/list',
      { includeCatalog: true, includeDisabled: true },
      20_000,
      {
        ownerId: owner.webContents.id,
        supportsDynamicToolRebind: callbacks.getConnectionStatus().capabilities?.dynamicToolRebind === true
      }
    )
    const protocol = result.apps
      ?.find((app) => app.appId === params.appId)
      ?.nativeApp?.protocol?.trim()
      .replace(/:$/, '')
      .toLowerCase()
    let url: URL
    try {
      url = new URL(params.url)
    } catch {
      throw new Error('Invalid app URL.')
    }
    if (!protocol || url.protocol.toLowerCase() !== `${protocol}:`) {
      throw new Error('App URL does not match the app native protocol.')
    }
    await openAppHandoffUrl(params.url, owner.webContents.id)
  })
}

import { app, ipcMain } from 'electron'
import * as path from 'path'
import type { PluginDesktopReadResult, PluginListResult } from '@dotcraft/sdk/contracts'
import type { DesktopAppServerClient } from './DesktopAppServerClient'
import type { IpcHandlerCallbacks } from './ipcBridge'
import { DesktopPluginArtifactCache } from './desktopPluginArtifactCache'
import { DesktopPluginModules } from './desktopPluginModules'

const channels = {
  context: 'desktop-plugin:context',
  setTrusted: 'desktop-plugin:set-trusted',
  registerModule: 'desktop-plugin:register-module',
  removeModule: 'desktop-plugin:remove-module'
}
let modules: DesktopPluginModules | null = null

export function registerDesktopPluginModuleIpc(
  getClient: () => DesktopAppServerClient | null,
  callbacks: IpcHandlerCallbacks | undefined,
  workspacePath: string
): void {
  unregisterDesktopPluginModuleIpc()
  modules = new DesktopPluginModules({
    connection() {
      const client = getClient()
      if (!client) throw new Error('AppServer is not connected.')
      const settings = callbacks?.getSettings()
      const workspace = callbacks?.getWorkspaceStatus()
      const remote = !!workspace?.remote || settings?.connectionMode === 'remote'
      const metadata = workspace?.remote
      let source = metadata?.projectId ?? 'local'
      if (remote && !metadata?.projectId) {
        const url = new URL(settings?.remote?.url ?? '')
        url.username = ''
        url.password = ''
        url.search = ''
        url.hash = ''
        source = url.toString()
      }
      return {
        identity: client, remote, source, workspacePath,
        supported: callbacks?.getConnectionStatus().capabilities?.desktopPluginArtifacts === true,
        list: () => client.sendRequest<PluginListResult>('plugin/list', { includeDisabled: true }),
        read: params => client.sendRequest<PluginDesktopReadResult>('plugin/desktop/read', params)
      }
    },
    cache: new DesktopPluginArtifactCache(path.join(app.getPath('userData'), 'desktop-plugin-cache')),
    grants: () => {
      const grants = callbacks?.getSettings().remoteDesktopPluginGrants
      return Array.isArray(grants) ? grants.filter(key => typeof key === 'string' && /^[0-9a-f]{64}$/.test(key)) : []
    },
    saveGrants: async grants => {
      if (!callbacks) throw new Error('Desktop settings are unavailable.')
      await callbacks.updateSettings({ remoteDesktopPluginGrants: grants })
    }
  })
  const manager = modules
  ipcMain.handle(channels.context, () => manager.context())
  ipcMain.handle(channels.setTrusted, (_event, params: { sourceKey: string; trusted: boolean }) =>
    manager.setTrusted(params.sourceKey, params.trusted))
  ipcMain.handle(channels.registerModule, (_event, params) => manager.register(params))
  ipcMain.handle(channels.removeModule, async (_event, params) => {
    await manager.remove(params)
    return { ok: true }
  })
}

export function unregisterDesktopPluginModuleIpc(): void {
  modules?.dispose()
  modules = null
  for (const channel of Object.values(channels)) ipcMain.removeHandler(channel)
}

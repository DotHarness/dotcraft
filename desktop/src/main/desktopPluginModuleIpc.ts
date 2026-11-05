import { app } from 'electron'
import * as path from 'path'
import type { PluginDesktopReadResult, PluginListResult } from '@dotcraft/sdk/contracts'
import type { DesktopAppServerClient } from './DesktopAppServerClient'
import type { IpcHandlerCallbacks } from './ipcBridge'
import type { WindowIpcMain } from './windowIpc'
import { DesktopPluginArtifactCache } from './desktopPluginArtifactCache'
import { DesktopPluginModules } from './desktopPluginModules'

const channels = {
  context: 'desktop-plugin:context',
  setTrusted: 'desktop-plugin:set-trusted',
  registerModule: 'desktop-plugin:register-module',
  removeModule: 'desktop-plugin:remove-module'
}

interface WindowPluginBinding {
  getClient: () => DesktopAppServerClient | null
  callbacks: IpcHandlerCallbacks
  workspacePath: string
  ipc: WindowIpcMain
  modules: DesktopPluginModules
}

const windows = new Map<number, WindowPluginBinding>()

export function registerDesktopPluginModuleIpc(
  getClient: () => DesktopAppServerClient | null,
  callbacks: IpcHandlerCallbacks,
  workspacePath: string,
  ipc: WindowIpcMain,
  ownerId: number
): void {
  let binding = windows.get(ownerId)
  if (binding) {
    removeHandlers(binding.ipc)
    binding.modules.prepareConnectionChange(
      !!callbacks.getWorkspaceStatus().remote || callbacks.getSettings().connectionMode === 'remote'
    )
    Object.assign(binding, { getClient, callbacks, workspacePath, ipc })
  } else {
    binding = { getClient, callbacks, workspacePath, ipc, modules: undefined! }
    const current = binding
    binding.modules = new DesktopPluginModules({
      ownerId,
      connection() {
        const { getClient, callbacks, workspacePath } = current
        const client = getClient()
        if (!client) throw new Error('AppServer is not connected.')
        const settings = callbacks.getSettings()
        const workspace = callbacks.getWorkspaceStatus()
        const remote = !!workspace.remote || settings.connectionMode === 'remote'
        const metadata = workspace.remote
        let source = metadata?.projectId ?? 'local'
        if (remote && !metadata?.projectId) {
          const url = new URL(settings.remote?.url ?? '')
          url.username = ''
          url.password = ''
          url.search = ''
          url.hash = ''
          source = url.toString()
        }
        return {
          identity: client,
          remote,
          source,
          workspacePath,
          supported: callbacks.getConnectionStatus().capabilities?.desktopPluginArtifacts === true,
          list: () => client.sendRequest<PluginListResult>('plugin/list', { includeDisabled: true }),
          read: (params) => client.sendRequest<PluginDesktopReadResult>('plugin/desktop/read', params)
        }
      },
      cache: new DesktopPluginArtifactCache(path.join(app.getPath('userData'), 'desktop-plugin-cache')),
      grants: () => {
        const grants = current.callbacks.getSettings().remoteDesktopPluginGrants
        return Array.isArray(grants)
          ? grants.filter((key) => typeof key === 'string' && /^[0-9a-f]{64}$/.test(key))
          : []
      },
      saveGrants: async (grants) => {
        const { callbacks } = current
        await callbacks.updateSettings({ remoteDesktopPluginGrants: grants })
      }
    })
    windows.set(ownerId, binding)
  }
  const manager = binding.modules
  ipc.handle(channels.context, () => manager.context())
  ipc.handle(channels.setTrusted, (_event, params: { sourceKey: string; trusted: boolean }) =>
    manager.setTrusted(params.sourceKey, params.trusted)
  )
  ipc.handle(channels.registerModule, (_event, params) => manager.register(params))
  ipc.handle(channels.removeModule, async (_event, params) => {
    await manager.remove(params)
    return { ok: true }
  })
}

export function unregisterDesktopPluginModuleIpc(preserveModules: boolean | undefined, ownerId: number): void {
  const binding = windows.get(ownerId)
  if (!binding) return
  removeHandlers(binding.ipc)
  if (preserveModules) binding.modules.prepareConnectionChange()
  else {
    binding.modules.dispose()
    windows.delete(ownerId)
  }
}

function removeHandlers(ipc: WindowIpcMain): void {
  for (const channel of Object.values(channels)) ipc.removeHandler(channel)
}

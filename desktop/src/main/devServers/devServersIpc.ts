import { existsSync, watch, type FSWatcher } from 'node:fs'
import { isAbsolute, join } from 'node:path'
import type { BrowserWindow, IpcMain } from 'electron'
import { DEV_SERVERS_CHANGED_CHANNEL, type DevServerScope } from '../../shared/devServers'
import { devServerManager } from './devServerManager'
import { isListening } from './ports'

const WATCH_DEBOUNCE_MS = 150

interface DevServersIpcDeps {
  owner: BrowserWindow
  handleSafe(channel: string, listener: Parameters<IpcMain['handle']>[1]): void
}

function folderOf(value: unknown): string {
  if (typeof value !== 'string' || !isAbsolute(value)) throw new Error('A dev server folder must be an absolute path.')
  return value
}

function scopeOf(value: unknown): DevServerScope {
  const scope = value as DevServerScope
  return { folder: folderOf(scope.folder), threadId: scope.threadId }
}

function watchLaunchFile(folder: string, onChange: () => void): () => void {
  let watcher: FSWatcher | null = null
  let target = ''
  let timer: ReturnType<typeof setTimeout> | null = null
  const fire = (): void => {
    if (timer) clearTimeout(timer)
    timer = setTimeout(() => {
      timer = null
      rewatch()
      onChange()
    }, WATCH_DEBOUNCE_MS)
  }
  const rewatch = (): void => {
    const craft = join(folder, '.craft')
    const next = existsSync(craft) ? craft : folder
    if (next === target && watcher) return
    watcher?.close()
    target = next
    try {
      watcher = watch(next, fire)
      watcher.on('error', () => undefined)
    } catch {
      watcher = null
    }
  }
  rewatch()
  return () => {
    if (timer) clearTimeout(timer)
    watcher?.close()
  }
}

export function registerDevServersHandlers(deps: DevServersIpcDeps): () => void {
  const { handleSafe, owner } = deps
  const sender = owner.webContents
  const watches = new Map<string, { count: number; dispose: () => void }>()
  let disposed = false
  const notify = (): void => {
    if (!disposed && !sender.isDestroyed()) sender.send(DEV_SERVERS_CHANGED_CHANNEL)
  }
  devServerManager.on('change', notify)

  handleSafe('dev-servers:list', (_event, scope: unknown) => devServerManager.list(scopeOf(scope)))
  handleSafe('dev-servers:start', (_event, params: { scope: unknown; name: string }) =>
    devServerManager.start(scopeOf(params.scope), params.name))
  handleSafe('dev-servers:stop', (_event, serverId: string) => devServerManager.stop(serverId))
  handleSafe('dev-servers:stop-scope', (_event, scope: unknown) => devServerManager.stopScope(scopeOf(scope)))
  handleSafe('dev-servers:stop-folder', (_event, folder: unknown) => devServerManager.stopFolder(folderOf(folder)))
  handleSafe('dev-servers:logs', (_event, serverId: string) => devServerManager.logs(serverId))
  handleSafe('dev-servers:is-listening', (_event, port: number) => isListening(port))
  handleSafe('dev-servers:rebind-thread', (_event, params: { fromThreadId: string; toThreadId: string }) =>
    devServerManager.rebindThread(params.fromThreadId, params.toThreadId))
  handleSafe('dev-servers:subscribe', (_event, folder: unknown) => {
    const path = folderOf(folder)
    const entry = watches.get(path)
    if (entry) {
      entry.count += 1
      return
    }
    watches.set(path, { count: 1, dispose: watchLaunchFile(path, notify) })
  })
  handleSafe('dev-servers:unsubscribe', (_event, folder: unknown) => {
    const path = folderOf(folder)
    const entry = watches.get(path)
    if (!entry) return
    entry.count -= 1
    if (entry.count > 0) return
    entry.dispose()
    watches.delete(path)
  })

  const dispose = (): void => {
    if (disposed) return
    disposed = true
    devServerManager.off('change', notify)
    for (const entry of watches.values()) entry.dispose()
    watches.clear()
    sender.removeListener('destroyed', dispose)
  }
  sender.once('destroyed', dispose)
  return dispose
}

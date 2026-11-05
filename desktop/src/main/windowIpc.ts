import { ipcMain, type BrowserWindow, type IpcMain } from 'electron'

type InvokeHandler = Parameters<IpcMain['handle']>[1]
type EventHandler = Parameters<IpcMain['on']>[1]

export interface WindowIpcMain {
  handle(channel: string, listener: InvokeHandler): void
  on(channel: string, listener: EventHandler): WindowIpcMain
  removeHandler(channel: string): void
  dispose(): void
}

interface Invocation {
  scope: WindowIpcMain
  listener: InvokeHandler
}
interface Subscription {
  scope: WindowIpcMain
  listener: EventHandler
}
interface EventChannel {
  listener: EventHandler
  owners: Map<number, Subscription[]>
}

const invocations = new Map<string, Map<number, Invocation>>()
const subscriptions = new Map<string, EventChannel>()

export function createWindowIpcMain(owner: BrowserWindow): WindowIpcMain {
  const ownerId = owner.webContents.id
  let disposed = false
  const active = (): boolean => !disposed && !owner.isDestroyed() && !owner.webContents.isDestroyed()
  const assertActive = (): void => {
    if (!active()) throw new Error('The Desktop IPC window is unavailable.')
  }
  const scope: WindowIpcMain = {
    handle(channel, listener) {
      assertActive()
      let owners = invocations.get(channel)
      if (!owners) {
        owners = new Map()
        invocations.set(channel, owners)
        ipcMain.handle(channel, (event, ...args: unknown[]) => {
          const invocation = invocations.get(channel)?.get(event.sender.id)
          if (!invocation) throw new Error('IPC request does not belong to an active Desktop window.')
          return invocation.listener(event, ...args)
        })
      }
      owners.set(ownerId, {
        scope,
        listener: (event, ...args: unknown[]) => {
          assertActive()
          return listener(event, ...args)
        }
      })
    },
    on(channel, listener) {
      assertActive()
      let entry = subscriptions.get(channel)
      if (!entry) {
        const owners = new Map<number, Subscription[]>()
        const dispatch: EventHandler = (event, ...args: unknown[]) => {
          for (const subscription of [...(owners.get(event.sender.id) ?? [])]) {
            subscription.listener(event, ...args)
          }
        }
        entry = { listener: dispatch, owners }
        subscriptions.set(channel, entry)
        ipcMain.on(channel, dispatch)
      }
      const current = entry.owners.get(ownerId) ?? []
      current.push({
        scope,
        listener: (event, ...args: unknown[]) => {
          if (active()) listener(event, ...args)
        }
      })
      entry.owners.set(ownerId, current)
      return scope
    },
    removeHandler(channel) {
      const owners = invocations.get(channel)
      if (owners?.get(ownerId)?.scope !== scope) return
      owners.delete(ownerId)
      if (owners.size === 0) {
        ipcMain.removeHandler(channel)
        invocations.delete(channel)
      }
    },
    dispose() {
      if (disposed) return
      disposed = true
      for (const channel of invocations.keys()) scope.removeHandler(channel)
      for (const channel of subscriptions.keys()) removeSubscriptions(channel)
      owner.removeListener('closed', scope.dispose)
    }
  }

  function removeSubscriptions(channel: string): void {
    const entry = subscriptions.get(channel)
    const current = entry?.owners.get(ownerId)
    if (!entry || !current) return
    const remaining = current.filter((subscription) => subscription.scope !== scope)
    if (remaining.length > 0) entry.owners.set(ownerId, remaining)
    else entry.owners.delete(ownerId)
    if (entry.owners.size === 0) {
      ipcMain.removeListener(channel, entry.listener)
      subscriptions.delete(channel)
    }
  }

  owner.once('closed', scope.dispose)
  return scope
}

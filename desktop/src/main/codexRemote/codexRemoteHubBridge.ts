import { ipcMain, type IpcMainEvent, type WebContents } from 'electron'
import type { CodexRemoteEvent, CodexRemoteStatus } from '../../shared/codexRemote'
import type { DesktopHubClient, HubEvent } from '../desktopHub'

const RETRY_INTERVAL_MS = 1000
const EVENT_CHANNEL = 'codexRemote:event'

export class CodexRemoteHubBridge {
  private readonly subscribers = new Map<WebContents, () => void>()
  private subscription: AbortController | null = null
  private retryTimer: ReturnType<typeof setTimeout> | null = null
  private revision = 0

  constructor(private getHubClient: () => DesktopHubClient) {}

  updateClient(getHubClient: () => DesktopHubClient): void {
    this.getHubClient = getHubClient
  }

  acquire(sender: WebContents): void {
    if (this.subscribers.has(sender)) return
    const destroyed = (): void => this.release(sender)
    this.subscribers.set(sender, destroyed)
    sender.once('destroyed', destroyed)
    if (this.subscribers.size === 1) this.startSubscription()
  }

  release(sender: WebContents): void {
    const destroyed = this.subscribers.get(sender)
    if (!destroyed) return
    sender.removeListener('destroyed', destroyed)
    this.subscribers.delete(sender)
    if (this.subscribers.size === 0) this.stopSubscription()
  }

  dispose(): void {
    for (const sender of this.subscribers.keys()) this.release(sender)
    this.stopSubscription()
  }

  private stopSubscription(): void {
    this.subscription?.abort()
    this.subscription = null
    if (this.retryTimer) clearTimeout(this.retryTimer)
    this.retryTimer = null
  }

  private startSubscription(): void {
    if (this.subscription || this.subscribers.size === 0) return
    const controller = new AbortController()
    this.subscription = controller
    const client = this.getHubClient()
    const finish = (): void => {
      if (this.subscription !== controller) return
      this.subscription = null
      this.retryTimer = setTimeout(() => {
        this.retryTimer = null
        this.startSubscription()
      }, RETRY_INTERVAL_MS)
    }
    void client.subscribeEvents(
      (event) => {
        if (!controller.signal.aborted) this.handleEvent(event)
      },
      controller.signal,
      () => {
        const revision = this.revision
        void client.getCodexRemote().then((status) => {
          if (!controller.signal.aborted && this.subscription === controller && revision === this.revision)
            this.publish({ kind: 'stateChanged', status })
        }, () => {})
      }
    ).then(finish, finish)
  }

  private handleEvent(event: HubEvent): void {
    if (event.kind === 'codexRemote.stateChanged') {
      this.revision += 1
      this.publish({ kind: 'stateChanged', status: event.data as CodexRemoteStatus })
    } else if (event.kind === 'codexRemote.paired') {
      this.publish({ kind: 'paired' })
    }
  }

  private publish(event: CodexRemoteEvent): void {
    for (const sender of this.subscribers.keys()) {
      if (!sender.isDestroyed()) sender.send(EVENT_CHANNEL, event)
    }
  }
}

let sharedBridge: CodexRemoteHubBridge | null = null
const subscribe = (event: IpcMainEvent): void => sharedBridge?.acquire(event.sender)
const unsubscribe = (event: IpcMainEvent): void => sharedBridge?.release(event.sender)

export function getCodexRemoteHubBridge(getHubClient: () => DesktopHubClient): CodexRemoteHubBridge {
  if (!sharedBridge) {
    sharedBridge = new CodexRemoteHubBridge(getHubClient)
    ipcMain.on('codexRemote:subscribe', subscribe)
    ipcMain.on('codexRemote:unsubscribe', unsubscribe)
  } else {
    sharedBridge.updateClient(getHubClient)
  }
  return sharedBridge
}

export function disposeCodexRemoteHubBridge(): void {
  sharedBridge?.dispose()
  sharedBridge = null
  ipcMain.removeListener('codexRemote:subscribe', subscribe)
  ipcMain.removeListener('codexRemote:unsubscribe', unsubscribe)
}

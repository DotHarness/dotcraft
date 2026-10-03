import { BrowserWindow } from 'electron'
import { toMobileEvent, toMobileStatus, type MobileEvent } from '../../shared/mobile'
import type { DesktopHubClient, HubEvent } from '../desktopHub'

const FALLBACK_POLL_INTERVAL_MS = 30_000

const MOBILE_EVENT_CHANNEL = 'mobile:event'

export interface MobileHubBridgeDeps {
  getHubClient: () => DesktopHubClient
  broadcast?: (event: MobileEvent) => void
}

function broadcastToWindows(event: MobileEvent): void {
  for (const window of BrowserWindow.getAllWindows()) {
    if (!window.isDestroyed()) window.webContents.send(MOBILE_EVENT_CHANNEL, event)
  }
}

export class MobileHubBridge {
  private deps: MobileHubBridgeDeps
  private refCount = 0
  private subscription: AbortController | null = null
  private pollTimer: ReturnType<typeof setInterval> | null = null

  constructor(deps: MobileHubBridgeDeps) {
    this.deps = deps
  }

  updateDeps(deps: MobileHubBridgeDeps): void {
    this.deps = deps
  }

  acquire(): void {
    this.refCount += 1
    if (this.refCount === 1) this.startSubscription()
  }

  release(): void {
    this.refCount = Math.max(0, this.refCount - 1)
    if (this.refCount > 0) return
    this.subscription?.abort()
    this.subscription = null
    this.stopFallbackPoll()
  }

  private startSubscription(): void {
    if (this.subscription) return
    const controller = new AbortController()
    this.subscription = controller
    this.stopFallbackPoll()

    const finish = (): void => {
      if (this.subscription !== controller) return
      this.subscription = null
      if (this.refCount > 0) this.startFallbackPoll()
    }
    void this.deps
      .getHubClient()
      .subscribeEvents((event) => this.handleHubEvent(event), controller.signal)
      .then(finish, finish)
  }

  private startFallbackPoll(): void {
    if (this.pollTimer) return
    this.pollTimer = setInterval(() => {
      void this.pollStatus().finally(() => this.startSubscription())
    }, FALLBACK_POLL_INTERVAL_MS)
  }

  private stopFallbackPoll(): void {
    if (!this.pollTimer) return
    clearInterval(this.pollTimer)
    this.pollTimer = null
  }

  private async pollStatus(): Promise<void> {
    const state = await this.deps.getHubClient().getMobile().catch(() => null)
    if (state) this.publish({ kind: 'stateChanged', status: toMobileStatus(state) })
  }

  private handleHubEvent(raw: HubEvent): void {
    const event = toMobileEvent(raw)
    if (event) this.publish(event)
  }

  private publish(event: MobileEvent): void {
    ;(this.deps.broadcast ?? broadcastToWindows)(event)
  }
}

let sharedBridge: MobileHubBridge | null = null

export function getMobileHubBridge(deps: MobileHubBridgeDeps): MobileHubBridge {
  if (sharedBridge) sharedBridge.updateDeps(deps)
  else sharedBridge = new MobileHubBridge(deps)
  return sharedBridge
}

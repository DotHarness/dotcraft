import {
  isTerminalUnavailableReason,
  parseScreenViewCapability,
  readScreenFrame,
  screenViewControl,
  FRAME_ACK_TIMEOUT_MS,
  type ScreenFrame,
  type ScreenViewStatus
} from '../../shared/screenView'
import type { ScreenSocket, ScreenSocketFactory, ScreenSocketTarget } from './screenSocket'

const BACKOFF_START_MS = 1000
const BACKOFF_CEILING_MS = 8000
const PAUSED_RETRY_MS = 5000
const SLOW_RETRY_MS = 10_000

export interface ScreenViewSessionOptions {
  peerId: string
  maxWidth: number
  watchers: number
  resolveBridge(peerId: string, sessionId: string): Promise<ScreenSocketTarget>
  createSocket: ScreenSocketFactory
  newSessionId(): string
  onState(status: ScreenViewStatus): void
  onFrame(frame: ScreenFrame): void
}

export interface ScreenViewSessionLike {
  start(): void
  setMaxWidth(maxWidth: number): void
  setWatchers(watchers: number): void
  ack(): void
  close(): void
}

export class ScreenViewSession implements ScreenViewSessionLike {
  private socket: ScreenSocket | null = null
  private maxWidth: number
  private watchers: number
  private backoffMs = BACKOFF_START_MS
  private retryTimer: ReturnType<typeof setTimeout> | null = null
  private ackTimer: ReturnType<typeof setTimeout> | null = null
  private pendingFrame: ScreenFrame | null = null
  private awaitingAck = false
  private duplicateRetried = false
  private stopped = false
  private status: ScreenViewStatus = { kind: 'connecting' }

  constructor(private readonly options: ScreenViewSessionOptions) {
    this.maxWidth = options.maxWidth
    this.watchers = options.watchers
  }

  start(): void {
    if (this.stopped) return
    this.publish({ kind: 'connecting' })
    void this.dial()
  }

  setMaxWidth(maxWidth: number): void {
    if (this.maxWidth === maxWidth) return
    this.maxWidth = maxWidth
    this.sendControl()
  }

  setWatchers(watchers: number): void {
    if (this.watchers === watchers) return
    this.watchers = watchers
    this.sendControl()
  }

  ack(): void {
    this.releaseCredit()
  }

  close(): void {
    if (this.stopped) return
    this.stopped = true
    this.clearRetry()
    this.clearAckTimer()
    this.pendingFrame = null
    const socket = this.socket
    this.socket = null
    if (!socket) return
    // The satellite stops capturing on `watchers = 0`, before it sees the close.
    socket.sendText(JSON.stringify(screenViewControl(0, this.maxWidth)))
    socket.close()
  }

  private async dial(): Promise<void> {
    if (this.stopped) return
    const sessionId = this.options.newSessionId()
    let target: ScreenSocketTarget
    try {
      target = await this.options.resolveBridge(this.options.peerId, sessionId)
    } catch {
      this.failAndRetry()
      return
    }
    if (this.stopped) return

    this.socket = this.options.createSocket(target, {
      onOpen: () => this.handleOpen(),
      onText: (text) => this.handleText(text),
      onBinary: (data) => this.handleBinary(data),
      onHandshakeStatus: (status) => this.handleHandshakeStatus(status),
      onClose: (_code, reason) => this.handleClose(reason),
      onFailure: () => this.failAndRetry()
    })
  }

  private handleOpen(): void {
    if (this.stopped) return
    this.backoffMs = BACKOFF_START_MS
    this.duplicateRetried = false
    this.publish({ kind: 'connecting' })
    this.sendControl()
  }

  private handleText(text: string): void {
    const capability = parseScreenViewCapability(text)
    if (!capability) return
    const reason = capability.unavailableReason
    if (!reason) {
      this.publish({ kind: 'connecting' })
      return
    }
    this.publish({ kind: 'unavailable', reason, ...(capability.detail ? { detail: capability.detail } : {}) })
    if (isTerminalUnavailableReason(reason)) this.stopForGood()
  }

  private handleBinary(data: Uint8Array): void {
    const frame = readScreenFrame(data)
    if (!frame) return
    this.deliver(frame)
  }

  private handleHandshakeStatus(status: number): void {
    this.socket = null
    // 404: the Hub knows no such peer. 503: it is enrolled but not dialled in right now.
    if (status === 404 || status === 503) {
      this.publish({ kind: 'offline' })
      this.scheduleRetry(SLOW_RETRY_MS)
      return
    }
    if (status === 409 && !this.duplicateRetried) {
      // A duplicate session id is the Hub's other 409; a fresh uuid settles it.
      this.duplicateRetried = true
      void this.dial()
      return
    }
    if (status === 409) {
      this.publish({ kind: 'unavailable', reason: 'noCaptureBackend' })
      this.stopForGood()
      return
    }
    if (status === 401 || status === 400) {
      this.publish({ kind: 'unavailable', reason: 'hubRejected' })
      this.stopForGood()
      return
    }
    this.failAndRetry()
  }

  private handleClose(reason: string): void {
    this.socket = null
    this.clearAckTimer()
    this.pendingFrame = null
    this.awaitingAck = false
    if (this.stopped) return

    if (reason === 'sharingPaused') {
      this.publish({ kind: 'paused' })
      this.scheduleRetry(PAUSED_RETRY_MS)
      return
    }
    if (reason === 'authorizationRequired') {
      this.publish({ kind: 'paused', needsAuthorization: true })
      this.scheduleRetry(SLOW_RETRY_MS)
      return
    }
    this.failAndRetry()
  }

  private failAndRetry(): void {
    this.socket = null
    this.publish({ kind: 'reconnecting' })
    const wait = this.backoffMs
    this.backoffMs = Math.min(BACKOFF_CEILING_MS, this.backoffMs * 2)
    this.scheduleRetry(wait)
  }

  private scheduleRetry(delayMs: number): void {
    if (this.stopped) return
    this.clearRetry()
    this.retryTimer = setTimeout(() => {
      this.retryTimer = null
      void this.dial()
    }, delayMs)
  }

  private stopForGood(): void {
    this.clearRetry()
    this.clearAckTimer()
    this.stopped = true
    const socket = this.socket
    this.socket = null
    socket?.close()
  }

  private sendControl(): void {
    this.socket?.sendText(JSON.stringify(screenViewControl(this.watchers, this.maxWidth)))
  }

  private deliver(frame: ScreenFrame): void {
    if (this.awaitingAck) {
      this.pendingFrame = frame
      return
    }
    this.awaitingAck = true
    this.clearAckTimer()
    this.ackTimer = setTimeout(() => {
      this.ackTimer = null
      this.releaseCredit()
    }, FRAME_ACK_TIMEOUT_MS)
    this.options.onFrame(frame)
  }

  private releaseCredit(): void {
    this.clearAckTimer()
    this.awaitingAck = false
    const pending = this.pendingFrame
    this.pendingFrame = null
    if (pending) this.deliver(pending)
  }

  private clearAckTimer(): void {
    if (!this.ackTimer) return
    clearTimeout(this.ackTimer)
    this.ackTimer = null
  }

  private clearRetry(): void {
    if (!this.retryTimer) return
    clearTimeout(this.retryTimer)
    this.retryTimer = null
  }

  private publish(status: ScreenViewStatus): void {
    if (
      this.status.kind === status.kind &&
      this.status.reason === status.reason &&
      this.status.detail === status.detail &&
      this.status.needsAuthorization === status.needsAuthorization
    ) {
      return
    }
    this.status = status
    this.options.onState(status)
  }
}

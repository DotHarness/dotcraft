import { randomUUID } from 'node:crypto'
import type {
  DesktopToolConfirmDetails,
  DesktopToolConfirmOutcome,
  DesktopToolConfirmRequest
} from '../shared/desktopToolConfirm'
import { readBrowserTurnNotification } from './browserTabLifecycle'

export interface DesktopToolConfirmPromptOptions {
  ownerId: number
  threadId?: string
  turnId?: string
  details: DesktopToolConfirmDetails
  sendToRenderer(request: DesktopToolConfirmRequest): boolean
  dismissInRenderer(requestId: string): void
  notifyAttention?(requestId: string): void
}

interface PendingConfirmation {
  requestId: string
  ownerId: number
  threadId?: string
  turnId?: string
  dismissInRenderer(requestId: string): void
  resolve(outcome: DesktopToolConfirmOutcome): void
}

interface QueuedConfirmation {
  ownerId: number
  threadId?: string
  turnId?: string
  canceled: boolean
  cancel(): void
}

export class DesktopToolConfirmManager {
  private readonly queues = new Map<number, Promise<void>>()
  private readonly queued = new Set<QueuedConfirmation>()
  private readonly pending = new Map<string, PendingConfirmation>()

  confirm(options: DesktopToolConfirmPromptOptions): Promise<DesktopToolConfirmOutcome> {
    const previous = this.queues.get(options.ownerId) ?? Promise.resolve()
    let releaseQueue!: () => void
    const current = new Promise<void>((resolve) => {
      releaseQueue = resolve
    })
    const tail = previous.then(() => current)
    this.queues.set(options.ownerId, tail)

    let resolveOutcome!: (outcome: DesktopToolConfirmOutcome) => void
    const outcomePromise = new Promise<DesktopToolConfirmOutcome>((resolve) => {
      resolveOutcome = resolve
    })

    const queuedEntry: QueuedConfirmation = {
      ownerId: options.ownerId,
      threadId: options.threadId,
      turnId: options.turnId,
      canceled: false,
      cancel: () => {
        queuedEntry.canceled = true
        this.queued.delete(queuedEntry)
        resolveOutcome('abandoned')
      }
    }
    this.queued.add(queuedEntry)

    void previous.then(() => {
      this.queued.delete(queuedEntry)
      if (queuedEntry.canceled) {
        releaseQueue()
        this.cleanupQueue(options.ownerId, tail)
        return
      }

      const requestId = randomUUID()
      const delivered = options.sendToRenderer({ ...options.details, requestId })
      if (!delivered) {
        resolveOutcome('abandoned')
        releaseQueue()
        this.cleanupQueue(options.ownerId, tail)
        return
      }

      this.pending.set(requestId, {
        requestId,
        ownerId: options.ownerId,
        threadId: options.threadId,
        turnId: options.turnId,
        dismissInRenderer: options.dismissInRenderer,
        resolve: (outcome) => {
          resolveOutcome(outcome)
          releaseQueue()
          this.cleanupQueue(options.ownerId, tail)
        }
      })
      options.notifyAttention?.(requestId)
    })

    return outcomePromise
  }

  respond(ownerId: number, requestId: string, confirmed: boolean): boolean {
    const entry = this.pending.get(requestId)
    if (!entry || entry.ownerId !== ownerId) return false
    this.pending.delete(requestId)
    entry.dismissInRenderer(requestId)
    entry.resolve(confirmed ? 'confirmed' : 'canceled')
    return true
  }

  observeNotification(method: string, params: unknown): void {
    const turn = readBrowserTurnNotification(method, params)
    if (turn?.terminal) this.abandonTurn(turn.threadId, turn.turnId)
  }

  observeUserRequest(method: string, params: unknown): void {
    if (method !== 'turn/interrupt' || !params || typeof params !== 'object') return
    const record = params as Record<string, unknown>
    if (typeof record.threadId === 'string' && typeof record.turnId === 'string') {
      this.abandonTurn(record.threadId, record.turnId)
    }
  }

  abandonTurn(threadId: string, turnId?: string): void {
    for (const entry of [...this.queued]) {
      if (entry.threadId === threadId && (!turnId || entry.turnId === turnId)) {
        entry.cancel()
      }
    }
    for (const entry of [...this.pending.values()]) {
      if (entry.threadId === threadId && (!turnId || entry.turnId === turnId)) {
        this.abandon(entry)
      }
    }
  }

  abandonOwner(ownerId: number): void {
    for (const entry of [...this.queued]) {
      if (entry.ownerId === ownerId) entry.cancel()
    }
    for (const entry of [...this.pending.values()]) {
      if (entry.ownerId === ownerId) this.abandon(entry)
    }
  }

  abandonAll(): void {
    for (const entry of [...this.queued]) entry.cancel()
    for (const entry of [...this.pending.values()]) this.abandon(entry)
  }

  private abandon(entry: PendingConfirmation): void {
    this.pending.delete(entry.requestId)
    entry.dismissInRenderer(entry.requestId)
    entry.resolve('abandoned')
  }

  private cleanupQueue(ownerId: number, tail: Promise<void>): void {
    if (this.queues.get(ownerId) === tail) this.queues.delete(ownerId)
  }
}

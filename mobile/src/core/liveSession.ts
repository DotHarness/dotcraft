import type { Timers } from './backoff'
import { isLive, needsYou, type ChatState } from './chatState'
import { runningChats, stateOf, type Action, type ChatSummary, type MobileState, type PendingRequest } from './state'
import type { Store } from './store'

export const LIVE_END_MS = 120_000

export interface LiveStatus {
  computer: string
  running: number
  needsYou: number
  reachable: boolean
}

export type LiveNotice =
  | { id: string; kind: 'request'; chat: ChatSummary; request: PendingRequest; alert: boolean }
  | { id: string; kind: 'turnEnded'; chat: ChatSummary; failed: boolean }

export type LiveAction = { type: 'allow' | 'reject'; key: string; requestId: string } | { type: 'end' }

export interface LiveNotifier {
  prepare(): Promise<void>
  start(status: LiveStatus): boolean
  update(status: LiveStatus): void
  stop(): void
  post(notice: LiveNotice): void
  cancel(id: string): void
  subscribe(listener: (action: LiveAction) => void): () => void
}

export interface LiveSessionHost {
  store: Store<MobileState, Action>
  timers: Timers
  openChat(key: string): void
  closeChat(key: string): void
  decide(key: string, requestId: string, decision: 'once' | 'reject'): void
  ended(): void
}

function liveChats(state: MobileState): ChatSummary[] {
  return runningChats(state).filter((chat) => isLive(stateOf(chat)))
}

function statusOf(state: MobileState): LiveStatus {
  const live = liveChats(state).map(stateOf)
  return {
    computer: state.computer?.name ?? '',
    running: live.filter((chat) => chat === 'running').length,
    needsYou: live.filter(needsYou).length,
    reachable: state.link === 'online',
  }
}

export class LiveSession {
  private foreground = true
  private active = false
  private prepared = false
  private evaluating = false
  private dirty = false
  private status = ''
  private idle: unknown = null
  private unreachable: unknown = null
  private readonly held = new Set<string>()
  private readonly tracked = new Set<string>()
  private readonly ended = new Map<string, ChatState>()
  private readonly shown = new Set<string>()
  private readonly alerted = new Set<string>()
  private readonly quiet = new Set<string>()

  constructor(
    private readonly notifier: LiveNotifier,
    private readonly host: LiveSessionHost,
  ) {
    host.store.subscribe(() => this.changed())
    notifier.subscribe((action) => this.act(action))
  }

  get running(): boolean {
    return this.active
  }

  enterBackground(): boolean {
    this.foreground = false
    const state = this.host.store.getState()
    if (state.link !== 'online' || liveChats(state).length === 0) return false
    const status = statusOf(state)
    if (!this.notifier.start(status)) return false
    this.active = true
    this.status = JSON.stringify(status)
    for (const chat of runningChats(state)) if (needsYou(stateOf(chat))) this.quiet.add(chat.key)
    this.changed()
    return true
  }

  enterForeground(): boolean {
    this.foreground = true
    if (!this.active) return false
    this.finish()
    return true
  }

  private act(action: LiveAction): void {
    if (action.type === 'end') this.end()
    else this.host.decide(action.key, action.requestId, action.type === 'allow' ? 'once' : 'reject')
  }

  private end(): void {
    if (!this.active) return
    this.finish()
    this.host.ended()
  }

  private finish(): void {
    this.active = false
    this.notifier.stop()
    for (const id of this.shown) this.notifier.cancel(id)
    for (const key of this.held) this.host.closeChat(key)
    this.idle = this.arm(this.idle, false)
    this.unreachable = this.arm(this.unreachable, false)
    for (const set of [this.held, this.tracked, this.shown, this.alerted, this.quiet]) set.clear()
    this.ended.clear()
  }

  private changed(): void {
    if (this.evaluating) {
      this.dirty = true
      return
    }
    this.evaluating = true
    try {
      do {
        this.dirty = false
        this.evaluate(this.host.store.getState())
      } while (this.dirty)
    } finally {
      this.evaluating = false
    }
  }

  private evaluate(state: MobileState): void {
    if (this.foreground) {
      if (!this.prepared && state.link === 'online' && liveChats(state).length > 0) {
        this.prepared = true
        void this.notifier.prepare()
      }
      return
    }
    if (!this.active) return
    if (!state.computer || state.identityChanged) {
      this.end()
      return
    }
    const live = liveChats(state)
    this.hold(new Set(live.map((chat) => chat.key)))
    this.notifyRequests(state)
    this.notifyTurnEnds(state)
    const status = statusOf(state)
    if (JSON.stringify(status) !== this.status) {
      this.status = JSON.stringify(status)
      this.notifier.update(status)
    }
    this.idle = this.arm(this.idle, live.length === 0)
    this.unreachable = this.arm(this.unreachable, state.link !== 'online')
  }

  private hold(keys: Set<string>): void {
    for (const key of keys) {
      if (this.held.has(key)) continue
      this.held.add(key)
      this.host.openChat(key)
    }
    for (const key of [...this.held]) {
      if (keys.has(key)) continue
      this.held.delete(key)
      this.host.closeChat(key)
    }
  }

  private notifyRequests(state: MobileState): void {
    const pending = new Map<string, { chat: ChatSummary; request: PendingRequest }>()
    for (const [key, requests] of Object.entries(state.pending)) {
      const chat = state.chats[key]
      if (chat) for (const request of requests) pending.set(`request:${key}:${request.requestId}`, { chat, request })
    }
    for (const id of [...this.shown]) {
      if (pending.has(id)) continue
      this.shown.delete(id)
      this.notifier.cancel(id)
    }
    for (const [id, { chat, request }] of pending) {
      if (this.shown.has(id)) continue
      const alert = !this.alerted.has(id) && !this.quiet.has(chat.key)
      this.quiet.delete(chat.key)
      this.alerted.add(id)
      this.shown.add(id)
      this.notifier.post({ id, kind: 'request', chat, request, alert })
    }
    for (const key of [...this.quiet]) {
      const chat = state.chats[key]
      if (!chat || !needsYou(stateOf(chat))) this.quiet.delete(key)
    }
  }

  private notifyTurnEnds(state: MobileState): void {
    for (const chat of runningChats(state)) {
      const current = stateOf(chat)
      if (isLive(current)) {
        this.tracked.add(chat.key)
        this.ended.delete(chat.key)
      } else if (this.tracked.has(chat.key) && this.ended.get(chat.key) !== current) {
        this.ended.set(chat.key, current)
        this.notifier.post({ id: `turn:${chat.key}`, kind: 'turnEnded', chat, failed: current === 'failed' })
      }
    }
  }

  private arm(handle: unknown, armed: boolean): unknown {
    if (armed) return handle ?? this.host.timers.setTimeout(() => this.end(), LIVE_END_MS)
    if (handle !== null) this.host.timers.clearTimeout(handle)
    return null
  }
}

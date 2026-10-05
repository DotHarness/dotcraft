import type { Timers } from './backoff'
import { isLive, needsYou, type ChatState } from './chatState'
import type { ChatHistory } from './history'
import { runningChats, stateOf, type Action, type ChatSummary, type ComputerState, type MobileState, type PendingRequest } from './state'
import type { Store } from './store'
import { latestActivity, type LatestActivity } from './transcript'

export const LIVE_END_MS = 120_000

export interface LiveFocus {
  computerId: string
  chat: ChatSummary
  state: ChatState
  request: PendingRequest | null
  activity: LatestActivity | null
}

export interface LiveStatus {
  computer: string
  running: number
  needsYou: number
  reachable: boolean
  focus: LiveFocus | null
}

interface NoticeChat {
  id: string
  computerId: string
  computer: string | null
  chat: ChatSummary
}

export type LiveNotice =
  | (NoticeChat & { kind: 'request'; request: PendingRequest; alert: boolean })
  | (NoticeChat & { kind: 'turnEnded'; failed: boolean })

export type LiveAction = { type: 'allow' | 'reject'; computerId: string; key: string; requestId: string } | { type: 'end' }

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
  openChat(computerId: string, key: string): void
  closeChat(computerId: string, key: string): void
  decide(computerId: string, key: string, requestId: string, decision: 'once' | 'reject'): void
  ended(): void
}

interface Entry {
  computerId: string
  computer: ComputerState
  chat: ChatSummary
}

function entriesOf(state: MobileState, chats: (computer: ComputerState) => ChatSummary[]): Entry[] {
  return state.order.flatMap((computerId) => {
    const computer = state.computers[computerId]
    return computer.identityChanged ? [] : chats(computer).map((chat) => ({ computerId, computer, chat }))
  })
}

function liveEntries(state: MobileState): Entry[] {
  return entriesOf(state, (computer) => runningChats(computer).filter((chat) => isLive(stateOf(chat))))
}

function heldKey({ computerId, chat }: Entry): string {
  return `${computerId}\n${chat.key}`
}

function splitHeld(held: string): { computerId: string; key: string } {
  const index = held.indexOf('\n')
  return { computerId: held.slice(0, index), key: held.slice(index + 1) }
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
  private activity: { history: ChatHistory; latest: LatestActivity | null } | null = null

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
    if (!liveEntries(state).some((entry) => entry.computer.link === 'online')) return false
    const status = this.statusOf(state)
    if (!this.notifier.start(status)) return false
    this.active = true
    this.status = JSON.stringify(status)
    for (const entry of entriesOf(state, runningChats)) if (needsYou(stateOf(entry.chat))) this.quiet.add(heldKey(entry))
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
    else this.host.decide(action.computerId, action.key, action.requestId, action.type === 'allow' ? 'once' : 'reject')
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
    for (const held of this.held) {
      const { computerId, key } = splitHeld(held)
      this.host.closeChat(computerId, key)
    }
    this.idle = this.arm(this.idle, false)
    this.unreachable = this.arm(this.unreachable, false)
    for (const set of [this.held, this.tracked, this.shown, this.alerted, this.quiet]) set.clear()
    this.ended.clear()
    this.activity = null
  }

  private statusOf(state: MobileState): LiveStatus {
    const live = liveEntries(state)
    const states = live.map((entry) => stateOf(entry.chat))
    const followed = live.find((entry) => needsYou(stateOf(entry.chat))) ?? live[0]
    const computer = followed?.computer ?? (state.order[0] ? state.computers[state.order[0]] : null)
    return {
      computer: computer?.computer.name ?? '',
      running: states.filter((value) => value === 'running').length,
      needsYou: states.filter(needsYou).length,
      reachable: computer?.link === 'online',
      focus: followed
        ? {
            computerId: followed.computerId,
            chat: followed.chat,
            state: stateOf(followed.chat),
            request: followed.computer.pending[followed.chat.key]?.[0] ?? null,
            activity: this.latestOf(followed.computer.details[followed.chat.key]?.history),
          }
        : null,
    }
  }

  private latestOf(history: ChatHistory | undefined): LatestActivity | null {
    if (!history) return null
    if (this.activity?.history !== history) this.activity = { history, latest: latestActivity(history) }
    return this.activity.latest
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
      if (!this.prepared && liveEntries(state).some((entry) => entry.computer.link === 'online')) {
        this.prepared = true
        void this.notifier.prepare()
      }
      return
    }
    if (!this.active) return
    if (state.order.length === 0) {
      this.end()
      return
    }
    const live = liveEntries(state)
    this.hold(new Set(live.map(heldKey)))
    this.notifyRequests(state)
    this.notifyTurnEnds(state)
    const status = this.statusOf(state)
    if (JSON.stringify(status) !== this.status) {
      this.status = JSON.stringify(status)
      this.notifier.update(status)
    }
    this.idle = this.arm(this.idle, live.length === 0)
    this.unreachable = this.arm(this.unreachable, live.length > 0 && !live.some((entry) => entry.computer.link === 'online'))
  }

  private hold(keys: Set<string>): void {
    for (const held of keys) {
      if (this.held.has(held)) continue
      this.held.add(held)
      const { computerId, key } = splitHeld(held)
      this.host.openChat(computerId, key)
    }
    for (const held of [...this.held]) {
      if (keys.has(held)) continue
      this.held.delete(held)
      const { computerId, key } = splitHeld(held)
      this.host.closeChat(computerId, key)
    }
  }

  private named(state: MobileState, computer: ComputerState): string | null {
    return state.order.length > 1 ? computer.computer.name : null
  }

  private notifyRequests(state: MobileState): void {
    const pending = new Map<string, { entry: Entry; request: PendingRequest }>()
    for (const computerId of state.order) {
      const computer = state.computers[computerId]
      for (const [key, requests] of Object.entries(computer.pending)) {
        const chat = computer.chats[key]
        if (chat) for (const request of requests) pending.set(`request:${computerId}:${key}:${request.requestId}`, { entry: { computerId, computer, chat }, request })
      }
    }
    for (const id of [...this.shown]) {
      if (pending.has(id)) continue
      this.shown.delete(id)
      this.notifier.cancel(id)
    }
    for (const [id, { entry, request }] of pending) {
      if (this.shown.has(id)) continue
      const alert = !this.alerted.has(id) && !this.quiet.has(heldKey(entry))
      this.alerted.add(id)
      this.shown.add(id)
      this.notifier.post({ id, kind: 'request', computerId: entry.computerId, computer: this.named(state, entry.computer), chat: entry.chat, request, alert })
    }
    for (const held of [...this.quiet]) {
      const { computerId, key } = splitHeld(held)
      const chat = state.computers[computerId]?.chats[key]
      if (!chat || !needsYou(stateOf(chat))) this.quiet.delete(held)
    }
  }

  private notifyTurnEnds(state: MobileState): void {
    for (const entry of entriesOf(state, runningChats)) {
      const current = stateOf(entry.chat)
      const held = heldKey(entry)
      if (isLive(current)) {
        this.tracked.add(held)
        this.ended.delete(held)
      } else if (this.tracked.has(held) && this.ended.get(held) !== current) {
        this.ended.set(held, current)
        this.notifier.post({
          id: `turn:${entry.computerId}:${entry.chat.key}`,
          kind: 'turnEnded',
          computerId: entry.computerId,
          computer: this.named(state, entry.computer),
          chat: entry.chat,
          failed: current === 'failed',
        })
      }
    }
  }

  private arm(handle: unknown, armed: boolean): unknown {
    if (armed) return handle ?? this.host.timers.setTimeout(() => this.end(), LIVE_END_MS)
    if (handle !== null) this.host.timers.clearTimeout(handle)
    return null
  }
}

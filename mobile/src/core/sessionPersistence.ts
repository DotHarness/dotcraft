import type { Timers } from './backoff'
import { samePersistedState, SnapshotProjector } from './persistenceSnapshot'
import type { StateStorage } from './session'
import type { Action, MobileState } from './state'
import type { Store } from './store'

const SAVE_IDLE_MS = 400
const SAVE_MAX_MS = 2000
const TERMINAL = new Set(['completed', 'failed', 'cancelled'])

function needsImmediateSave(previous: MobileState, next: MobileState): boolean {
  if (previous.order.some((id) => !next.computers[id])) return true
  for (const id of next.order) {
    const before = previous.computers[id]
    const after = next.computers[id]
    if (!before || before === after) continue
    if (before.chats !== after.chats && Object.values(after.chats).some((chat) =>
      before.chats[chat.key]?.runtime?.running && !chat.runtime?.running,
    )) return true
    if (before.details === after.details) continue
    for (const [key, detail] of Object.entries(after.details)) {
      const old = before.details[key]
      if (!old || old.history.turns === detail.history.turns) continue
      const statuses = new Map(old.history.turns.map((turn) => [turn.id, turn.status]))
      if (detail.history.turns.some((turn) => TERMINAL.has(turn.status) && statuses.get(turn.id) !== turn.status)) return true
    }
  }
  return false
}

export class SessionPersistence {
  private latest: MobileState
  private version = 0
  private savedVersion = 0
  private requestedVersion = 0
  private attemptedVersion = 0
  private writing: Promise<void> | null = null
  private idle: unknown = null
  private maximum: unknown = null
  private readonly projector = new SnapshotProjector()
  private readonly unsubscribe: () => void

  constructor(
    private readonly storage: StateStorage,
    private readonly timers: Timers,
    store: Store<MobileState, Action>,
  ) {
    this.latest = store.getState()
    this.unsubscribe = store.subscribe(() => this.changed(store.getState()))
  }

  private changed(next: MobileState): void {
    const previous = this.latest
    this.latest = next
    if (!next.hydrated || samePersistedState(previous, next)) return
    this.version += 1
    if (needsImmediateSave(previous, next)) {
      void this.flush()
      return
    }
    if (this.idle !== null) this.timers.clearTimeout(this.idle)
    this.idle = this.timers.setTimeout(() => void this.flush(), SAVE_IDLE_MS)
    this.maximum ??= this.timers.setTimeout(() => void this.flush(), SAVE_MAX_MS)
  }

  flush(): Promise<void> {
    this.cancelTimers()
    if (!this.latest.hydrated || this.version === this.savedVersion) return Promise.resolve()
    this.requestedVersion = this.version
    this.writing ??= this.drain().finally(() => {
      this.writing = null
      if (this.requestedVersion > this.attemptedVersion) return this.flush()
    })
    return this.writing
  }

  private async drain(): Promise<void> {
    while (this.savedVersion < this.requestedVersion) {
      const version = this.version
      this.attemptedVersion = version
      const state = this.latest
      try {
        if (state.order.length === 0) await this.storage.clear()
        else await this.storage.save(this.projector.project(state))
        this.savedVersion = version
      } catch {
        if (this.requestedVersion <= version) return
      }
    }
  }

  private cancelTimers(): void {
    if (this.idle !== null) this.timers.clearTimeout(this.idle)
    if (this.maximum !== null) this.timers.clearTimeout(this.maximum)
    this.idle = this.maximum = null
  }

  dispose(): void {
    this.unsubscribe()
    void this.flush()
  }
}

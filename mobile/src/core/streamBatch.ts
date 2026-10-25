import { systemTimers, type Timers } from './backoff'
import type { HistoryDelta } from './history'

export const STREAM_WINDOW_MS = 50

interface PendingStream {
  timer: unknown
  deltas: Map<string, HistoryDelta>
}

export class StreamBatch {
  private readonly pending = new Map<string, PendingStream>()

  constructor(
    private readonly commit: (threadId: string, deltas: HistoryDelta[]) => void,
    private readonly timers: Timers = systemTimers,
  ) {}

  push(threadId: string, delta: HistoryDelta): void {
    if (!delta.delta) return
    let stream = this.pending.get(threadId)
    if (!stream) {
      stream = { timer: this.timers.setTimeout(() => this.flush(threadId), STREAM_WINDOW_MS), deltas: new Map() }
      this.pending.set(threadId, stream)
    }
    const key = JSON.stringify([delta.turnId, delta.itemId])
    const previous = stream.deltas.get(key)
    stream.deltas.set(key, previous ? { ...previous, delta: previous.delta + delta.delta } : delta)
  }

  flush(threadId?: string): void {
    if (threadId === undefined) {
      for (const id of [...this.pending.keys()]) this.flush(id)
      return
    }
    const stream = this.pending.get(threadId)
    if (!stream) return
    this.pending.delete(threadId)
    this.timers.clearTimeout(stream.timer)
    this.commit(threadId, [...stream.deltas.values()])
  }
}

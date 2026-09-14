export type BrowserTabKeepStatus = 'handoff' | 'deliverable'

export interface LifecycleTab {
  id: string
  adopted?: boolean
  userOwned?: boolean
  keptStatus?: BrowserTabKeepStatus
}

interface LifecycleEffects<T> {
  release(tab: T, status?: BrowserTabKeepStatus): void
  retain(tab: T, status: BrowserTabKeepStatus): void
  close(tab: T): void
}

export function readBrowserTurnNotification(method: string, params: unknown): {
  threadId: string
  turnId: string
  terminal: boolean
} | undefined {
  if (!['turn/started', 'turn/completed', 'turn/failed', 'turn/cancelled'].includes(method)) return
  if (!params || typeof params !== 'object') return
  const envelope = params as Record<string, unknown>
  const turn = envelope.turn && typeof envelope.turn === 'object'
    ? envelope.turn as Record<string, unknown>
    : envelope
  const threadId = turn.threadId ?? envelope.threadId
  const turnId = turn.id ?? turn.turnId ?? envelope.turnId
  if (typeof threadId !== 'string' || typeof turnId !== 'string') return
  return { threadId, turnId, terminal: method !== 'turn/started' }
}

export class BrowserTabLifecycle {
  private turnId?: string
  private finished = false
  private used = false
  private readonly marks = new Map<string, BrowserTabKeepStatus>()

  beginTurn(turnId: string): void {
    if (turnId === this.turnId) return
    this.turnId = turnId
    this.finished = false
    this.used = false
    this.marks.clear()
  }

  recordUse(turnId: string): void {
    this.beginTurn(turnId)
    this.used = true
  }

  mark(tabId: string, status: BrowserTabKeepStatus): void {
    this.marks.set(tabId, status)
  }

  finalize<T extends LifecycleTab>(
    tabs: Iterable<T>,
    keep: ReadonlyMap<string, BrowserTabKeepStatus>,
    effects: LifecycleEffects<T>
  ): { ok: true; kept: string[]; released: string[]; closed: string[] } {
    const result = { ok: true as const, kept: [] as string[], released: [] as string[], closed: [] as string[] }
    this.marks.clear()
    for (const tab of [...tabs]) {
      const status = keep.get(tab.id)
      tab.keptStatus = status
      if (status) {
        this.marks.set(tab.id, status)
        result.kept.push(tab.id)
        if (status === 'deliverable' || tab.adopted || tab.userOwned) effects.release(tab, status)
        else effects.retain(tab, status)
      } else if (tab.adopted || tab.userOwned) {
        tab.userOwned = true
        tab.adopted = false
        effects.release(tab)
        result.released.push(tab.id)
      } else {
        effects.close(tab)
        result.closed.push(tab.id)
      }
    }
    return result
  }

  finishTurn<T extends LifecycleTab>(turnId: string, tabs: Iterable<T>, effects: LifecycleEffects<T>): boolean {
    if (turnId !== this.turnId || this.finished || !this.used) return false
    this.finalize(tabs, new Map(this.marks), effects)
    this.marks.clear()
    this.finished = true
    return true
  }
}

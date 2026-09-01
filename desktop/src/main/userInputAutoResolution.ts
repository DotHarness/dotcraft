import type { UserInputAutoResolutionState } from '../shared/userInputAutoResolution'

export const USER_INPUT_INACTIVITY_MS = 60_000
export const USER_INPUT_AUTO_RESOLUTION_MS = 90_000

interface TimerEntry extends UserInputAutoResolutionState {
  bridgeId: string
  timer: ReturnType<typeof setTimeout> | null
}

interface UserInputAutoResolutionOptions {
  onChanged: (states: UserInputAutoResolutionState[]) => void
  onResolve: (bridgeId: string) => void
}

export class UserInputAutoResolutionCoordinator {
  private readonly entries = new Map<string, TimerEntry>()
  private readonly onChanged: (states: UserInputAutoResolutionState[]) => void
  private readonly onResolve: (bridgeId: string) => void
  private presentedThreadId: string | null = null
  private windowFocused = false

  constructor(options: UserInputAutoResolutionOptions) {
    this.onChanged = options.onChanged
    this.onResolve = options.onResolve
  }

  getSnapshot(): UserInputAutoResolutionState[] {
    return [...this.entries.values()].map(({ bridgeId: _bridgeId, timer: _timer, ...state }) => state)
  }

  track(input: {
    bridgeId: string
    threadId: string
    requestId: string
    isBlocking: boolean
  }): void {
    if (input.isBlocking) return

    const entry: TimerEntry = {
      bridgeId: input.bridgeId,
      threadId: input.threadId,
      requestId: input.requestId,
      phase: 'scheduled',
      deadlineAt: null,
      timer: null
    }
    this.entries.set(input.bridgeId, entry)
    if (this.isForeground(input.threadId)) {
      this.startInactivityWait(entry)
    } else {
      this.startResolutionTimer(entry)
    }
    this.emitChanged()
  }

  setPresentedThread(threadId: string | null): void {
    if (this.presentedThreadId === threadId) return
    this.presentedThreadId = threadId
    this.reconcileForegroundState()
  }

  setWindowFocused(focused: boolean): void {
    if (this.windowFocused === focused) return
    this.windowFocused = focused
    this.reconcileForegroundState()
  }

  recordConversationActivity(threadId: string): void {
    let changed = false
    for (const entry of this.entries.values()) {
      if (
        entry.threadId === threadId
        && entry.phase === 'waitingForInactivity'
        && this.isForeground(threadId)
      ) {
        this.startInactivityWait(entry)
        changed = true
      }
    }
    if (changed) this.emitChanged()
  }

  snooze(threadId: string, requestId: string): void {
    let changed = false
    for (const entry of this.entries.values()) {
      if (entry.threadId !== threadId || entry.requestId !== requestId) continue
      this.clearTimer(entry)
      this.entries.delete(entry.bridgeId)
      changed = true
    }
    if (changed) this.emitChanged()
  }

  remove(bridgeId: string): void {
    const entry = this.entries.get(bridgeId)
    if (!entry) return
    this.clearTimer(entry)
    this.entries.delete(bridgeId)
    this.emitChanged()
  }

  clear(): void {
    if (this.entries.size === 0) return
    for (const entry of this.entries.values()) this.clearTimer(entry)
    this.entries.clear()
    this.emitChanged()
  }

  private reconcileForegroundState(): void {
    let changed = false
    for (const entry of this.entries.values()) {
      if (this.isForeground(entry.threadId)) {
        if (entry.phase === 'scheduled') {
          this.startInactivityWait(entry)
          changed = true
        }
      } else if (entry.phase === 'waitingForInactivity') {
        this.startResolutionTimer(entry)
        changed = true
      }
    }
    if (changed) this.emitChanged()
  }

  private isForeground(threadId: string): boolean {
    return this.windowFocused && this.presentedThreadId === threadId
  }

  private startInactivityWait(entry: TimerEntry): void {
    this.clearTimer(entry)
    entry.phase = 'waitingForInactivity'
    entry.deadlineAt = null
    entry.timer = setTimeout(() => {
      entry.timer = null
      this.startResolutionTimer(entry)
      this.emitChanged()
    }, USER_INPUT_INACTIVITY_MS)
  }

  private startResolutionTimer(entry: TimerEntry): void {
    this.clearTimer(entry)
    entry.phase = 'scheduled'
    entry.deadlineAt = Date.now() + USER_INPUT_AUTO_RESOLUTION_MS
    entry.timer = setTimeout(() => {
      entry.timer = null
      if (!this.entries.delete(entry.bridgeId)) return
      this.emitChanged()
      this.onResolve(entry.bridgeId)
    }, USER_INPUT_AUTO_RESOLUTION_MS)
  }

  private clearTimer(entry: TimerEntry): void {
    if (entry.timer == null) return
    clearTimeout(entry.timer)
    entry.timer = null
  }

  private emitChanged(): void {
    this.onChanged(this.getSnapshot())
  }
}

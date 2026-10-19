import type { ProviderAuthLoginState } from '@dotcraft/sdk/contracts'

const TERMINAL_STATUSES = new Set(['completed', 'cancelled', 'failed'])

export function isLoginTerminal(state: Pick<ProviderAuthLoginState, 'status'>): boolean {
  return TERMINAL_STATUSES.has(state.status)
}

export class ProviderLoginTracker {
  private providerId: string | null = null
  private starting = false
  private buffered: ProviderAuthLoginState[] = []
  private current: ProviderAuthLoginState | null = null

  begin(providerId: string): void {
    this.providerId = providerId.toLowerCase()
    this.starting = true
    this.buffered = []
    this.current = null
  }

  started(state: ProviderAuthLoginState): ProviderAuthLoginState {
    const candidates = [state, ...this.buffered.filter((entry) => entry.loginId === state.loginId)]
    this.starting = false
    this.buffered = []
    this.current = candidates.reduce((best, entry) => entry.revision > best.revision ? entry : best)
    return this.current
  }

  abandon(): void {
    this.starting = false
    this.buffered = []
  }

  receive(state: ProviderAuthLoginState): ProviderAuthLoginState | null {
    if (this.starting) {
      if (state.providerId?.toLowerCase() === this.providerId) this.buffered.push(state)
      return null
    }
    return this.merge(state)
  }

  merge(state: ProviderAuthLoginState): ProviderAuthLoginState | null {
    if (!this.current || state.loginId !== this.current.loginId || state.revision <= this.current.revision) return null
    this.current = state
    return state
  }

  get isStarting(): boolean {
    return this.starting
  }

  get activeLoginId(): string | null {
    return this.current && !isLoginTerminal(this.current) ? this.current.loginId : null
  }
}

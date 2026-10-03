export interface Timers {
  setTimeout(callback: () => void, ms: number): unknown
  clearTimeout(handle: unknown): void
}

export const systemTimers: Timers = {
  setTimeout: (callback, ms) => setTimeout(callback, ms),
  clearTimeout: (handle) => clearTimeout(handle as ReturnType<typeof setTimeout>),
}

export function reconnectDelay(attempt: number, random: () => number): number {
  const ceiling = Math.min(30_000, 1_000 * 2 ** attempt)
  return Math.round(Math.max(1_000, ceiling / 2 + random() * (ceiling / 2)))
}

export class Reconnector {
  private attempt = 0
  private handle: unknown = null

  constructor(
    private readonly run: () => void,
    private readonly timers: Timers,
    private readonly random: () => number = Math.random,
  ) {}

  get pending(): boolean {
    return this.handle !== null
  }

  schedule(): void {
    this.cancel()
    const delay = reconnectDelay(this.attempt, this.random)
    this.attempt += 1
    this.handle = this.timers.setTimeout(() => {
      this.handle = null
      this.run()
    }, delay)
  }

  now(): void {
    this.reset()
    this.run()
  }

  reset(): void {
    this.cancel()
    this.attempt = 0
  }

  cancel(): void {
    if (this.handle !== null) this.timers.clearTimeout(this.handle)
    this.handle = null
  }
}

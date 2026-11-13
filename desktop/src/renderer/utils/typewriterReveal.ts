interface RevealSnapshot {
  source: string
  text: string
}

export class TypewriterBuffer {
  private source: string
  private enabled: boolean
  private bufferedLength: number
  private snapshot: RevealSnapshot
  private listeners = new Set<() => void>()
  private frame: number | undefined
  private timeout: number | undefined
  private rate = 1
  private lastArrival: number | null = null
  private lastDrain: number | null = null
  private drainCarry = 0
  private lastTargetLength: number

  constructor(source: string, enabled: boolean) {
    this.source = source
    this.enabled = enabled
    this.bufferedLength = source.length
    this.lastTargetLength = source.length
    this.snapshot = { source, text: source }
  }

  getSnapshot = (): RevealSnapshot => this.snapshot

  subscribe = (listener: () => void): (() => void) => {
    this.listeners.add(listener)
    return () => this.listeners.delete(listener)
  }

  update(source: string, enabled: boolean): void {
    const reset = !source.startsWith(this.source) || (!this.enabled && enabled)
    this.source = source
    this.enabled = enabled

    if (reset) {
      this.destroy()
      this.bufferedLength = source.length
      this.lastTargetLength = source.length
      this.rate = 1
      this.lastArrival = null
      this.lastDrain = null
      this.drainCarry = 0
      this.publish()
    } else if (!enabled) {
      this.destroy()
      this.bufferedLength = source.length
      this.publish()
    } else if (this.bufferedLength < source.length) {
      this.drain()
    }
  }

  destroy(): void {
    window.clearTimeout(this.timeout)
    this.timeout = undefined
    if (this.frame !== undefined) {
      window.cancelAnimationFrame(this.frame)
      this.frame = undefined
    }
  }

  private drain(): void {
    const now = performance.now() / 1000
    const added = this.source.length - this.lastTargetLength
    this.lastTargetLength = this.source.length
    if (added > 0) {
      this.rate = this.lastArrival === null
        ? added
        : Math.max(1, 0.1 * added / Math.max(now - this.lastArrival, 0.01) + 0.9 * this.rate)
      this.lastArrival = now
    }

    const elapsed = this.lastDrain === null ? 0 : now - this.lastDrain
    this.lastDrain = now
    const backlog = this.source.length - this.bufferedLength
    for (let step = 0; step < (elapsed + this.drainCarry) / 0.05; step++) {
      if (backlog > 0 && backlog < this.rate * 1.5) {
        this.rate = (backlog + this.rate * 1.5) / 2
      }
      if (backlog > this.rate / 1.2) {
        this.rate = (backlog + this.rate / 1.2) / 2
      }
    }
    this.rate = Math.max(1, this.rate)

    const progress = elapsed * this.rate + this.drainCarry
    const advance = Math.floor(progress)
    this.drainCarry = progress - advance
    this.bufferedLength = Math.min(this.bufferedLength + advance, this.source.length)
    if (this.bufferedLength === this.source.length) {
      this.lastDrain = null
      this.drainCarry = 0
    }

    if (this.snapshot.text.length !== this.bufferedLength) {
      this.frame ??= window.requestAnimationFrame(() => {
        this.frame = undefined
        this.publish()
      })
    }
    this.timeout ??= window.setTimeout(() => {
      this.timeout = undefined
      if (this.bufferedLength < this.source.length) this.drain()
    }, 50)
  }

  private publish(): void {
    let end = this.bufferedLength
    const last = this.source.charCodeAt(end - 1)
    if (last >= 0xD800 && last <= 0xDBFF) end--
    const text = this.source.slice(0, end)
    if (text === this.snapshot.text && this.source === this.snapshot.source) return
    this.snapshot = { source: this.source, text }
    for (const listener of this.listeners) listener()
  }
}

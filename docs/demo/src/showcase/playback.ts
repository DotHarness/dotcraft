export type PlaybackState = 'playing' | 'paused' | 'ended'

export interface Cue {
  /** Milliseconds after the previous cue. */
  delay: number
  run: () => void
}

type Control = 'play' | 'pause' | 'restart'

function report(state: PlaybackState, progress: number): void {
  if (window.parent !== window) window.parent.postMessage({ type: 'dotcraft-showcase:playback', state, progress }, '*')
}

export class Timeline {
  private index = 0
  private elapsed = 0
  private startedAt = 0
  private timer: ReturnType<typeof setTimeout> | undefined
  private ticker: ReturnType<typeof setInterval> | undefined
  private state: PlaybackState = 'paused'
  private readonly total: number
  /** Script time skipped by `seek`, so timestamps taken during and after a seek keep their spacing. */
  skipped = 0

  constructor(private readonly cues: Cue[], private readonly reset: () => void) {
    this.total = cues.reduce((sum, cue) => sum + cue.delay, 0) || 1
    this.reset()
    report('paused', 0)
  }

  play(): void {
    if (this.state === 'playing') return
    if (this.state === 'ended') {
      this.restart()
      return
    }
    this.state = 'playing'
    this.schedule()
  }

  pause(): void {
    if (this.state !== 'playing') return
    this.stop()
    this.elapsed += Date.now() - this.startedAt
    this.state = 'paused'
    report('paused', this.progress())
  }

  restart(): void {
    this.stop()
    this.index = 0
    this.elapsed = 0
    this.reset()
    this.state = 'playing'
    this.schedule()
  }

  dispose(): void {
    this.stop()
  }

  /** Runs cues at once, without their delays, until `reached` holds after a cue; playback stays paused there. */
  seek(reached: () => boolean): void {
    this.stop()
    this.elapsed = 0
    while (this.index < this.cues.length) {
      this.skipped += this.cues[this.index].delay
      this.cues[this.index].run()
      this.index += 1
      if (reached()) break
    }
    this.state = this.index < this.cues.length ? 'paused' : 'ended'
    report(this.state, this.progress())
  }

  private stop(): void {
    clearTimeout(this.timer)
    clearInterval(this.ticker)
  }

  private progress(): number {
    const done = this.cues.slice(0, this.index).reduce((sum, cue) => sum + cue.delay, 0)
    const running = this.state === 'playing' ? Date.now() - this.startedAt : 0
    return Math.min(1, (done + this.elapsed + running) / this.total)
  }

  private schedule(): void {
    const cue = this.cues[this.index]
    if (!cue) {
      clearInterval(this.ticker)
      this.state = 'ended'
      report('ended', 1)
      return
    }
    this.startedAt = Date.now()
    report('playing', this.progress())
    clearInterval(this.ticker)
    this.ticker = setInterval(() => report('playing', this.progress()), 200)
    this.timer = setTimeout(() => {
      this.elapsed = 0
      this.index += 1
      cue.run()
      this.schedule()
    }, Math.max(0, cue.delay - this.elapsed))
  }
}

export function onPlaybackControl(current: () => Timeline | null): () => void {
  const listener = (event: MessageEvent): void => {
    if (event.source !== window.parent) return
    const data = event.data as { type?: string; action?: Control } | null
    if (data?.type !== 'dotcraft-showcase:control') return
    const timeline = current()
    if (!timeline) return
    if (data.action === 'play') timeline.play()
    else if (data.action === 'pause') timeline.pause()
    else if (data.action === 'restart') timeline.restart()
  }
  window.addEventListener('message', listener)
  return () => window.removeEventListener('message', listener)
}

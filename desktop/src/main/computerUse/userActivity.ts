export interface UserActivityProbe {
  cursor(): { x: number; y: number }
  idleSeconds(): number
  now(): number
}

const IDLE_RESOLUTION_MS = 1000

export class UserActivityGuard {
  private baseline: { at: number; cursor: { x: number; y: number } }

  constructor(private readonly probe: UserActivityProbe) {
    this.baseline = this.capture()
  }

  rebase(): void {
    this.baseline = this.capture()
  }

  check(): void {
    const cursor = this.probe.cursor()
    const moved = cursor.x !== this.baseline.cursor.x || cursor.y !== this.baseline.cursor.y
    const sinceBaseline = this.probe.now() - this.baseline.at
    const typed = this.probe.idleSeconds() * 1000 + IDLE_RESOLUTION_MS < sinceBaseline
    if (moved || typed) {
      throw new Error('user_input_detected: the user used the mouse or keyboard after your last observation, so no input was sent. Wait a few seconds, observe the window again with get_window_state, then continue.')
    }
  }

  private capture(): { at: number; cursor: { x: number; y: number } } {
    return { at: this.probe.now(), cursor: this.probe.cursor() }
  }
}

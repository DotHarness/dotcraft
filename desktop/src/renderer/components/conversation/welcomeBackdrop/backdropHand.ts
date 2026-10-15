import { MAX_TAPS } from './backdropShaders'

const TAP_LIFE_S = 3.5
const SLOW_PX_S = 60
const FAST_PX_S = 800
const WARMTH_FADE_S = 0.6

export interface HandFrame {
  pointer: [number, number, number]
  tapCount: number
  taps: Float32Array
}

function smooth01(value: number): number {
  const t = Math.min(1, Math.max(0, value))
  return t * t * (3 - 2 * t)
}

export function createBackdropHand(): {
  move: (x: number, y: number, now: number) => void
  tap: (x: number, y: number, now: number) => void
  leave: () => void
  busy: (now: number) => boolean
  rippling: (now: number) => boolean
  frame: (now: number) => HandFrame
} {
  let taps: Array<{ x: number; y: number; at: number }> = []
  let last: { x: number; y: number; at: number } | null = null
  let speed = 0
  let warmth = 0
  let warmthAt = 0
  const frame: HandFrame = { pointer: [0, 0, 0], tapCount: 0, taps: new Float32Array(MAX_TAPS * 4) }

  const warmthNow = (now: number): number => warmth * Math.exp(-Math.max(0, now - warmthAt) / WARMTH_FADE_S)

  return {
    move: (x, y, now) => {
      if (last && now - last.at < 0.25) {
        const dt = Math.max(now - last.at, 1 / 240)
        speed += (Math.hypot(x - last.x, y - last.y) / dt - speed) * Math.min(1, dt / 0.08)
      } else {
        speed = 0
      }
      last = { x, y, at: now }
      warmth = Math.max(warmthNow(now), smooth01((speed - SLOW_PX_S) / (FAST_PX_S - SLOW_PX_S)))
      warmthAt = now
      frame.pointer[0] = x
      frame.pointer[1] = y
    },
    tap: (x, y, now) => {
      taps = [...taps.slice(-(MAX_TAPS - 1)), { x, y, at: now }]
    },
    leave: () => {
      last = null
    },
    busy: (now) => taps.some((tap) => now - tap.at < TAP_LIFE_S) || warmthNow(now) > 0.01,
    rippling: (now) => taps.some((tap) => now - tap.at < TAP_LIFE_S),
    frame: (now) => {
      taps = taps.filter((tap) => now - tap.at < TAP_LIFE_S)
      frame.pointer[2] = warmthNow(now)
      frame.tapCount = taps.length
      taps.forEach((tap, index) => frame.taps.set([tap.x, tap.y, tap.at, 1], index * 4))
      return frame
    }
  }
}

export const THINKING_LOOP_MS = 3000
export const THINKING_REST = { spin: 0, drop: 0, core: 1.6 }

export interface ThinkingPose {
  spin: number
  drop: number
  core: number
}

type Stops = readonly (readonly [number, number])[]

const DROP: Stops = [[0, -7.3], [0.18, -7.3], [0.45, 0], [0.58, 0], [0.76, -8.2], [0.86, -7.3], [1, -7.3]]
const CORE: Stops = [[0, 0], [0.18, 0], [0.4, 1], [0.5, 1.5], [0.62, 1], [0.76, 0], [1, 0]]

function cubic(a: number, b: number, t: number): number {
  'worklet'
  return 3 * a * t * (1 - t) ** 2 + 3 * b * t ** 2 * (1 - t) + t ** 3
}

function bezier(x: number, x1: number, y1: number, x2: number, y2: number): number {
  'worklet'
  let low = 0
  let high = 1
  for (let step = 0; step < 24; step++) {
    const mid = (low + high) / 2
    if (cubic(x1, x2, mid) < x) low = mid
    else high = mid
  }
  return cubic(y1, y2, (low + high) / 2)
}

function keyframe(stops: Stops, progress: number, x1: number, x2: number): number {
  'worklet'
  const next = stops.findIndex(([offset]) => offset >= progress)
  if (next <= 0) return stops[Math.max(next, 0)][1]
  const [from, a] = stops[next - 1]
  const [to, b] = stops[next]
  return a + (b - a) * bezier((progress - from) / (to - from), x1, 0, x2, 1)
}

export function thinkingPose(progress: number): ThinkingPose {
  'worklet'
  return { spin: 120 * progress, drop: keyframe(DROP, progress, 0.5, 0.3), core: keyframe(CORE, progress, 0.42, 0.58) }
}

export function thinkingRotation(spin: number): number[] {
  'worklet'
  const angle = (spin * Math.PI) / 180
  const sin = Math.sin(angle)
  const cos = Math.cos(angle)
  return [cos, sin, -sin, cos, 12 - 12 * cos + 12 * sin, 12 - 12 * sin - 12 * cos]
}

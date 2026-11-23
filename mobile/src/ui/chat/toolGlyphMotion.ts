export type ToolGlyphMotion = 'command' | 'search' | 'read' | 'web'

export const TOOL_GLYPH_LOOP_MS: Record<ToolGlyphMotion, number> = {
  command: 1800,
  search: 1600,
  read: 1400,
  web: 2400,
}

type Stops = readonly (readonly [number, number])[]

const PROMPT_Y: Stops = [[0, 0], [0.36, 0], [0.48, -5], [0.485, 5], [0.8, 5], [0.92, 0], [1, 0]]
const PROMPT_OPACITY: Stops = [[0, 1], [0.36, 1], [0.48, 0], [0.8, 0], [0.92, 1], [1, 1]]
const OUTPUT_Y: Stops = [[0, 5], [0.36, 5], [0.48, 0], [0.8, 0], [0.92, -5], [0.925, 5], [1, 5]]
const OUTPUT_OPACITY: Stops = [[0, 0], [0.36, 0], [0.48, 1], [0.8, 1], [0.92, 0], [1, 0]]
const SCAN_X: Stops = [[0, 0], [0.25, -1.6], [0.5, 1.4], [0.75, 1.2], [1, 0]]
const SCAN_Y: Stops = [[0, 0], [0.25, -1.4], [0.5, -1.8], [0.75, 0.6], [1, 0]]
const LEAF: Stops = [[0, 1], [0.5, -1], [1, -1]]
const MERIDIAN: Stops = [[0, 1], [1, -1]]
const MERIDIAN_REST = 0.369

function cubic(a: number, b: number, t: number): number {
  'worklet'
  return 3 * a * t * (1 - t) ** 2 + 3 * b * t ** 2 * (1 - t) + t ** 3
}

function ease(x: number, x1: number, x2: number): number {
  'worklet'
  let low = 0
  let high = 1
  for (let step = 0; step < 20; step++) {
    const mid = (low + high) / 2
    if (cubic(x1, x2, mid) < x) low = mid
    else high = mid
  }
  return cubic(0, 1, (low + high) / 2)
}

function keyframe(stops: Stops, progress: number, x1: number, x2: number): number {
  'worklet'
  const next = stops.findIndex(([offset]) => offset >= progress)
  if (next <= 0) return stops[Math.max(next, 0)][1]
  const [from, a] = stops[next - 1]
  const [to, b] = stops[next]
  return a + (b - a) * ease((progress - from) / (to - from), x1, x2)
}

export function glyphMatrix(scaleX: number, x: number, y: number): number[] {
  'worklet'
  return [scaleX, 0, 0, 1, 12 - 12 * scaleX + x, y]
}

export function commandPose(progress: number) {
  'worklet'
  const blinking = progress < 0.36
  return {
    promptY: keyframe(PROMPT_Y, progress, 0.4, 0.2),
    promptOpacity: keyframe(PROMPT_OPACITY, progress, 0.4, 0.2),
    cursorOpacity: blinking && Math.floor(progress / 0.06) % 2 === 1 ? 0 : 1,
    outputY: keyframe(OUTPUT_Y, progress, 0.4, 0.2),
    outputOpacity: keyframe(OUTPUT_OPACITY, progress, 0.4, 0.2),
  }
}

export function scanPose(progress: number) {
  'worklet'
  return { x: keyframe(SCAN_X, progress, 0.42, 0.58), y: keyframe(SCAN_Y, progress, 0.42, 0.58) }
}

export function leafScale(progress: number): number {
  'worklet'
  return keyframe(LEAF, progress, 0.45, 0.35)
}

export function meridianScale(progress: number, trailing: boolean): number {
  'worklet'
  const phase = (progress + (trailing ? 1 - MERIDIAN_REST : MERIDIAN_REST)) % 1
  return keyframe(MERIDIAN, phase, 0.37, 0.63)
}

export function leafPath(scale: number): string {
  'worklet'
  const lift = 1.2 * (1 - Math.abs(scale))
  const p = (x: number, y: number) => `${(12 + scale * (x - 12)).toFixed(2)} ${(y - (lift * Math.abs(x - 12)) / 10).toFixed(2)}`
  return `M${p(12, 7)}C${p(12, 4.79)} ${p(13.79, 3)} ${p(16, 3)}L${p(21, 3)}C${p(21.55, 3)} ${p(22, 3.45)} ${p(22, 4)}L${p(22, 17)}C${p(22, 17.55)} ${p(21.55, 18)} ${p(21, 18)}L${p(15, 18)}C${p(13.34, 18)} ${p(12, 19.34)} ${p(12, 21)}Z`
}

export function meridianPath(scale: number): string {
  'worklet'
  const rx = 10 * scale
  const k = (12 + 0.5523 * rx).toFixed(2)
  const x = (12 + rx).toFixed(2)
  return `M12 2C${k} 2 ${x} 6.48 ${x} 12C${x} 17.52 ${k} 22 12 22`
}

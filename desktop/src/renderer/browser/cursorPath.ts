import type { SpringParams } from './cursorSpring'

export interface Point {
  x: number
  y: number
}

export interface Size {
  width: number
  height: number
}

interface Segment {
  control1: Point
  control2: Point
  end: Point
}

export interface CursorPath {
  start: Point
  end: Point
  arced: boolean
  segments: Segment[]
}

interface PathMetrics {
  length: number
  angleChangeEnergy: number
  maxAngleChange: number
  totalTurn: number
  staysInBounds: boolean
}

export const REST_TIP_ANGLE = -37

const PATH_DAMPING = 0.9
const MIN_RESPONSE = 0.12
const MAX_RESPONSE = 2.2
const RESPONSE_SCALE = 0.7
const START_HANDLE = 0.41960295031576633
const END_HANDLE = 0.15
const ARC_SIZE = 0.2765523188064277
const ARC_FLOW = 0.5783555327868779
const BOUNDS_MARGIN = 20
const SAMPLES_PER_SEGMENT = 24
const ARC_DISTANCE_SCALES = [0.55, 0.8, 1.05]
const ARC_HANDLE_SCALES = [0.65, 1, 1.35]

export function distanceBetween(a: Point, b: Point): number {
  return Math.hypot(b.x - a.x, b.y - a.y)
}

export function clamp(value: number, min: number, max: number): number {
  return Math.max(min, Math.min(max, value))
}

export function normalizeDegrees(degrees: number): number {
  const wrapped = degrees % 360
  return wrapped < 0 ? wrapped + 360 : wrapped
}

function directionFromAngle(degrees: number): Point {
  const radians = degrees * Math.PI / 180
  return { x: Math.sin(radians), y: -Math.cos(radians) }
}

export function unit(vector: Point): Point {
  const length = Math.hypot(vector.x, vector.y)
  return length < 0.001 ? { x: 1, y: 0 } : { x: vector.x / length, y: vector.y / length }
}

function offset(origin: Point, direction: Point, distance: number): Point {
  return { x: origin.x + direction.x * distance, y: origin.y + direction.y * distance }
}

function clipRay(bounds: Size, origin: Point, direction: Point, distance: number): Point {
  let reach = distance
  if (direction.x < 0) reach = Math.min(reach, origin.x / -direction.x)
  if (direction.x > 0) reach = Math.min(reach, (bounds.width - origin.x) / direction.x)
  if (direction.y < 0) reach = Math.min(reach, origin.y / -direction.y)
  if (direction.y > 0) reach = Math.min(reach, (bounds.height - origin.y) / direction.y)
  return offset(origin, direction, Math.max(0, reach))
}

function inBounds(point: Point, bounds: Size): boolean {
  return point.x >= BOUNDS_MARGIN && point.x <= bounds.width - BOUNDS_MARGIN &&
    point.y >= BOUNDS_MARGIN && point.y <= bounds.height - BOUNDS_MARGIN
}

function bezierPoint(p0: Point, segment: Segment, t: number): Point {
  const u = 1 - t
  const a = u * u * u
  const b = 3 * u * u * t
  const c = 3 * u * t * t
  const d = t * t * t
  return {
    x: p0.x * a + segment.control1.x * b + segment.control2.x * c + segment.end.x * d,
    y: p0.y * a + segment.control1.y * b + segment.control2.y * c + segment.end.y * d
  }
}

function bezierTangent(p0: Point, segment: Segment, t: number): Point {
  const u = 1 - t
  const { control1: c1, control2: c2, end } = segment
  return {
    x: 3 * u * u * (c1.x - p0.x) + 6 * u * t * (c2.x - c1.x) + 3 * t * t * (end.x - c2.x),
    y: 3 * u * u * (c1.y - p0.y) + 6 * u * t * (c2.y - c1.y) + 3 * t * t * (end.y - c2.y)
  }
}

export function samplePath(path: CursorPath, progress: number): { point: Point; tangent: Point } {
  const t = clamp(progress, 0, 1)
  const scaled = t === 1 ? path.segments.length - 1 : t * path.segments.length
  const index = Math.floor(scaled)
  const segment = path.segments[index]!
  const segmentStart = index === 0 ? path.start : path.segments[index - 1]!.end
  const local = t === 1 ? 1 : scaled - index
  return { point: bezierPoint(segmentStart, segment, local), tangent: bezierTangent(segmentStart, segment, local) }
}

export function tipAngleOf(tangent: Point): number {
  if (Math.hypot(tangent.x, tangent.y) < 0.001) return normalizeDegrees(REST_TIP_ANGLE)
  const heading = unit(tangent)
  return normalizeDegrees(Math.atan2(heading.y, heading.x) * 180 / Math.PI + 90)
}

function turnBetween(from: number, to: number): number {
  let turn = to - from
  while (turn > Math.PI) turn -= Math.PI * 2
  while (turn < -Math.PI) turn += Math.PI * 2
  return turn
}

function measurePath(path: CursorPath, bounds?: Size): PathMetrics {
  let length = 0
  let angleChangeEnergy = 0
  let maxAngleChange = 0
  let totalTurn = 0
  let previousAngle: number | null = null
  let staysInBounds = bounds === undefined || inBounds(path.start, bounds)
  let segmentStart = path.start
  let previous = path.start
  for (const segment of path.segments) {
    for (let step = 1; step <= SAMPLES_PER_SEGMENT; step += 1) {
      const point = bezierPoint(segmentStart, segment, step / SAMPLES_PER_SEGMENT)
      length += distanceBetween(previous, point)
      if (bounds) staysInBounds &&= inBounds(point, bounds)
      const dx = point.x - previous.x
      const dy = point.y - previous.y
      if (Math.hypot(dx, dy) > 0.01) {
        const angle = Math.atan2(dy, dx)
        if (previousAngle !== null) {
          const turn = turnBetween(previousAngle, angle)
          angleChangeEnergy += turn * turn
          maxAngleChange = Math.max(maxAngleChange, Math.abs(turn))
          totalTurn += Math.abs(turn)
        }
        previousAngle = angle
      }
      previous = point
    }
    segmentStart = segment.end
  }
  return { length, angleChangeEnergy, maxAngleChange, totalTurn, staysInBounds }
}

function reversalPenalty(path: CursorPath): number {
  const tip = directionFromAngle(REST_TIP_ANGLE)
  const heading = unit({ x: path.end.x - path.start.x, y: path.end.y - path.start.y })
  return clamp((-(heading.x * tip.x + heading.y * tip.y) - 0.08) / 0.92, 0, 1)
}

function scorePath(path: CursorPath, metrics: PathMetrics): number {
  const direct = Math.max(1, distanceBetween(path.start, path.end))
  const excess = Math.max(0, metrics.length / direct - 1)
  return metrics.length + excess * 320 + metrics.angleChangeEnergy * 140 + metrics.maxAngleChange * 180 +
    metrics.totalTurn * 18 + reversalPenalty(path) * 90 + (path.arced ? 45 : 0)
}

function straightCurve(start: Point, end: Point, startControl: Point, endControl: Point): CursorPath {
  return { start, end, arced: false, segments: [{ control1: startControl, control2: endControl, end }] }
}

function arcedCurve(start: Point, end: Point, startControl: Point, endControl: Point, arc: Point, heading: Point, handle: number): CursorPath {
  return {
    start,
    end,
    arced: true,
    segments: [
      { control1: startControl, control2: offset(arc, heading, -handle), end: arc },
      { control1: offset(arc, heading, handle), control2: endControl, end }
    ]
  }
}

function bestPath(candidates: CursorPath[], bounds: Size): CursorPath {
  let best = candidates[0]!
  let bestScore = Infinity
  let bestInside: CursorPath | undefined
  let bestInsideScore = Infinity
  for (const candidate of candidates) {
    const metrics = measurePath(candidate, bounds)
    const score = scorePath(candidate, metrics)
    if (score < bestScore) {
      best = candidate
      bestScore = score
    }
    if (metrics.staysInBounds && score < bestInsideScore) {
      bestInside = candidate
      bestInsideScore = score
    }
  }
  return bestInside ?? best
}

export function planCursorPath(start: Point, end: Point, bounds: Size): CursorPath {
  const tip = directionFromAngle(REST_TIP_ANGLE)
  const arrival = { x: -tip.x, y: -tip.y }
  const distance = distanceBetween(start, end)
  const heading = unit({ x: end.x - start.x, y: end.y - start.y })
  const startHandle = Math.max(48, Math.min(640, distance * START_HANDLE, distance * 0.9))
  const endHandle = Math.max(48, Math.min(640, distance * END_HANDLE, distance * 0.9))
  const startControl = clipRay(bounds, start, tip, startHandle)
  const endControl = clipRay(bounds, end, arrival, endHandle)
  const perpendicular = { x: -heading.y, y: heading.x }
  const side = perpendicular.x * tip.x + perpendicular.y * tip.y >= 0 ? 1 : -1
  const naturalNormal = { x: perpendicular.x * side, y: perpendicular.y * side }
  const midpoint = { x: (start.x + end.x) / 2, y: (start.y + end.y) / 2 }
  const arcDistance = Math.max(50, Math.min(520, distance * ARC_SIZE))
  const arcHandle = Math.max(38, Math.min(440, distance * ARC_FLOW))
  const candidates = [
    straightCurve(start, end, startControl, endControl),
    straightCurve(start, end, clipRay(bounds, start, tip, startHandle * 0.65), clipRay(bounds, end, arrival, endHandle * 0.65))
  ]
  for (const distanceScale of ARC_DISTANCE_SCALES) {
    for (const handleScale of ARC_HANDLE_SCALES) {
      for (const sign of [1, -1]) {
        const normal = { x: naturalNormal.x * sign, y: naturalNormal.y * sign }
        const apex = {
          x: midpoint.x + normal.x * arcDistance * distanceScale + tip.x * startHandle * 0.16,
          y: midpoint.y + normal.y * arcDistance * distanceScale + tip.y * startHandle * 0.16
        }
        candidates.push(arcedCurve(start, end, startControl, endControl, apex, heading, arcHandle * handleScale))
      }
    }
  }
  return bestPath(candidates, bounds)
}

export function pathSpring(path: CursorPath): SpringParams {
  const metrics = measurePath(path)
  const direct = Math.max(1, distanceBetween(path.start, path.end))
  const excess = Math.max(0, metrics.length / direct - 1)
  const lengthFactor = clamp((metrics.length - 180) / 760, 0, 1)
  const curviness = clamp(
    clamp(excess / 0.55, 0, 1) * 0.42 +
      clamp(metrics.totalTurn / (Math.PI * 1.4), 0, 1) * 0.38 +
      clamp(metrics.angleChangeEnergy / 1.25, 0, 1) * 0.2,
    0,
    1
  )
  const arcBonus = path.arced ? 0.04 : 0
  const arcFactor = path.arced ? 0.9 : 1
  const response = clamp(
    (0.42 + lengthFactor * 0.22 + curviness * 0.12 + reversalPenalty(path) * 0.28 + arcBonus) * RESPONSE_SCALE * arcFactor,
    MIN_RESPONSE,
    MAX_RESPONSE
  )
  return { dampingFraction: PATH_DAMPING, response }
}

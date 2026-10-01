import {
  clamp,
  distanceBetween,
  normalizeDegrees,
  pathSpring,
  planCursorPath,
  REST_TIP_ANGLE,
  samplePath,
  tipAngleOf,
  type CursorPath,
  type Point,
  type Size,
  unit
} from './cursorPath'
import {
  createSpring,
  isSettled,
  retune,
  snapSpring,
  steerAngle,
  stepSpring,
  type Spring,
  type SpringParams
} from './cursorSpring'

interface CursorPose {
  point: Point
  tipAngle: number
  scootAxis: number
  scootTilt: number
  scootSquash: number
  stretch: number
  visibility: number
}

type Motion =
  | { kind: 'path'; path: CursorPath; progress: Spring }
  | { kind: 'scoot'; start: Point; end: Point; axis: number; tiltTarget: number; progress: Spring }

const SCOOT_DISTANCE = 196
const ARRIVAL_DISTANCE = 0.85
const ARRIVAL_SPEED = 12
const ARRIVAL_PROGRESS = 0.999
const ARRIVAL_PROGRESS_SPEED = 0.01
const MIN_SPEED_FRAME = 1 / 240
const MAX_SCOOT_TILT = 70
const SCOOT_SQUASH = 0.15
const WOBBLE_DURATION = 1.41
const WOBBLE_PERIOD = 0.66
const WOBBLE_DEGREES = 12.5

const POSITION: SpringParams = { response: 0.19, dampingFraction: 0.9 }
const TIP: SpringParams = { response: 0.12, dampingFraction: 0.9 }
const STRETCH: SpringParams = { response: 0.2, dampingFraction: 0.85 }
const VISIBILITY: SpringParams = { response: 0.42, dampingFraction: 0.86 }
const SCOOT_PROGRESS: SpringParams = { response: 0.19, dampingFraction: 0.94 }
const SCOOT_TILT: SpringParams = { response: 0.055, dampingFraction: 0.82 }
const SCOOT_SQUASH_SPRING: SpringParams = { response: 0.12, dampingFraction: 0.86 }

export class CursorMotion {
  private point: Point
  private tipAngle = normalizeDegrees(REST_TIP_ANGLE)
  private scootAxis = 0
  private motion: Motion | null = null
  private wobbleStartedAt: number | null = null
  private readonly x: Spring
  private readonly y: Spring
  private readonly tip = createSpring(this.tipAngle, this.tipAngle, TIP)
  private readonly axis = createSpring(0, 0, TIP)
  private readonly tilt = createSpring(0, 0, SCOOT_TILT)
  private readonly squash = createSpring(1, 1, SCOOT_SQUASH_SPRING)
  private readonly stretch = createSpring(1, 1, STRETCH)
  private readonly visibility: Spring

  constructor(point: Point, visible: boolean) {
    this.point = point
    this.x = createSpring(point.x, point.x, POSITION)
    this.y = createSpring(point.y, point.y, POSITION)
    this.visibility = createSpring(+visible, +visible, VISIBILITY)
  }

  get position(): Point {
    return this.point
  }

  get active(): boolean {
    return this.motion !== null || this.wobbleStartedAt !== null ||
      ![this.x, this.y, this.tip, this.axis, this.tilt, this.squash, this.stretch, this.visibility].every(isSettled)
  }

  setVisible(visible: boolean, instant: boolean): void {
    this.visibility.target = +visible
    if (instant) snapSpring(this.visibility, +visible)
  }

  startWobble(now: number): void {
    this.wobbleStartedAt = now
  }

  snapTo(point: Point): void {
    this.motion = null
    this.wobbleStartedAt = null
    this.place(point)
    snapSpring(this.tip, normalizeDegrees(REST_TIP_ANGLE))
    this.tipAngle = this.tip.value
    this.resetScoot()
    snapSpring(this.stretch, 1)
  }

  glideTo(target: Point, bounds: Size): void {
    this.wobbleStartedAt = null
    const start = { ...this.point }
    if (distanceBetween(start, target) <= SCOOT_DISTANCE) {
      this.startScoot(start, target)
      return
    }
    const path = planCursorPath(start, target, bounds)
    const spring = pathSpring(path)
    this.tuneFollowers(clamp(spring.response * 0.18, 0.035, 0.12), spring.dampingFraction)
    this.motion = { kind: 'path', path, progress: createSpring(0, 1, spring) }
  }

  step(dt: number, now: number): boolean {
    const arrived = this.stepMotion(dt, now)
    for (const spring of [this.visibility, this.stretch, this.squash, this.tilt]) stepSpring(spring, dt)
    return arrived
  }

  pose(now: number): CursorPose {
    return {
      point: this.point,
      tipAngle: this.wobbled(now),
      scootAxis: this.scootAxis,
      scootTilt: this.tilt.value,
      scootSquash: this.squash.value,
      stretch: this.stretch.value,
      visibility: this.visibility.value
    }
  }

  private stepMotion(dt: number, now: number): boolean {
    if (!this.motion) {
      this.stretch.target = 1
      this.squash.target = 1
      this.tilt.target = 0
      return false
    }
    this.wobbleStartedAt = null
    return this.motion.kind === 'scoot' ? this.stepScoot(this.motion, dt, now) : this.stepPath(this.motion, dt, now)
  }

  private stepPath(motion: Extract<Motion, { kind: 'path' }>, dt: number, now: number): boolean {
    this.squash.target = 1
    this.tilt.target = 0
    stepSpring(motion.progress, dt)
    const progress = clamp(motion.progress.value, 0, 1)
    const sample = samplePath(motion.path, progress)
    this.x.target = sample.point.x
    this.y.target = sample.point.y
    steerAngle(this.tip, tipAngleOf(sample.tangent))
    steerAngle(this.axis, 0)
    const speed = this.stepFollowers(dt).speed
    this.stretch.target = clamp(1 - speed / 5500, 0.65, 1)
    if (progress < ARRIVAL_PROGRESS || Math.abs(motion.progress.velocity) >= ARRIVAL_PROGRESS_SPEED || !this.near(sample.point)) {
      return false
    }
    const end = samplePath(motion.path, 1)
    const tipAngle = tipAngleOf(end.tangent)
    this.place(end.point)
    snapSpring(this.tip, tipAngle)
    this.tipAngle = tipAngle
    this.resetScoot()
    snapSpring(this.stretch, 1)
    this.motion = null
    this.wobbleStartedAt = now
    return true
  }

  private stepScoot(motion: Extract<Motion, { kind: 'scoot' }>, dt: number, now: number): boolean {
    stepSpring(motion.progress, dt)
    this.x.target = motion.end.x
    this.y.target = motion.end.y
    steerAngle(this.axis, motion.axis)
    steerAngle(this.tip, normalizeDegrees(REST_TIP_ANGLE))
    const along = progressAlong(this.stepFollowers(dt).point, motion.start, motion.end)
    this.stretch.target = 1
    this.squash.target = 1 - SCOOT_SQUASH * Math.sin(clamp(along, 0, 1) * Math.PI)
    this.tilt.target = motion.tiltTarget * Math.sin(Math.min(1, along) * Math.PI)
    if (along < ARRIVAL_PROGRESS || Math.abs(motion.progress.velocity) >= ARRIVAL_PROGRESS_SPEED || !this.near(motion.end)) {
      return false
    }
    this.place(motion.end)
    snapSpring(this.tip, normalizeDegrees(REST_TIP_ANGLE))
    this.tipAngle = this.tip.value
    this.resetScoot()
    snapSpring(this.stretch, 1)
    this.motion = null
    this.wobbleStartedAt = now
    return true
  }

  private startScoot(start: Point, end: Point): void {
    const heading = unit({ x: end.x - start.x, y: end.y - start.y })
    const axis = Math.atan2(heading.y, heading.x) * 180 / Math.PI
    this.tuneFollowers(POSITION.response, POSITION.dampingFraction)
    this.x.target = end.x
    this.y.target = end.y
    steerAngle(this.tip, normalizeDegrees(REST_TIP_ANGLE))
    steerAngle(this.axis, axis)
    this.motion = {
      kind: 'scoot',
      start,
      end,
      axis,
      tiltTarget: clamp(heading.x * 0.75 - heading.y * 0.62, -1, 1) * MAX_SCOOT_TILT,
      progress: createSpring(0, 1, SCOOT_PROGRESS)
    }
  }

  private stepFollowers(dt: number): { point: Point; speed: number } {
    const previous = this.point
    for (const spring of [this.x, this.y, this.tip, this.axis]) stepSpring(spring, dt)
    const point = { x: this.x.value, y: this.y.value }
    this.point = point
    this.tipAngle = this.tip.value
    this.scootAxis = this.axis.value
    return { point, speed: distanceBetween(previous, point) / Math.max(dt, MIN_SPEED_FRAME) }
  }

  private tuneFollowers(response: number, dampingFraction: number): void {
    retune(this.x, { response, dampingFraction })
    retune(this.y, { response, dampingFraction })
  }

  private near(target: Point): boolean {
    return distanceBetween(this.point, target) <= ARRIVAL_DISTANCE &&
      Math.abs(this.x.velocity) <= ARRIVAL_SPEED && Math.abs(this.y.velocity) <= ARRIVAL_SPEED
  }

  private place(point: Point): void {
    this.point = point
    snapSpring(this.x, point.x)
    snapSpring(this.y, point.y)
  }

  private resetScoot(): void {
    snapSpring(this.axis, 0)
    snapSpring(this.tilt, 0)
    snapSpring(this.squash, 1)
    this.scootAxis = 0
  }

  private wobbled(now: number): number {
    if (this.wobbleStartedAt === null) return this.tipAngle
    const elapsed = (now - this.wobbleStartedAt) / 1000
    if (elapsed < 0) return this.tipAngle
    const progress = Math.min(1, elapsed / WOBBLE_DURATION)
    if (progress >= 1) {
      this.wobbleStartedAt = null
      return this.tipAngle
    }
    return this.tipAngle + Math.sin(elapsed / WOBBLE_PERIOD * Math.PI * 2) * Math.sin(progress * Math.PI) * WOBBLE_DEGREES
  }
}

function progressAlong(point: Point, start: Point, end: Point): number {
  const dx = end.x - start.x
  const dy = end.y - start.y
  const lengthSquared = dx * dx + dy * dy
  if (lengthSquared < 0.001) return 1
  return clamp(((point.x - start.x) * dx + (point.y - start.y) * dy) / lengthSquared, 0, 1)
}

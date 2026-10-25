import { useEffect, useId, useState } from 'react'
import { PixelRatio } from 'react-native'
import Svg, { Circle, Defs, FeColorMatrix, FeGaussianBlur, Filter, G } from 'react-native-svg'
import { useReducedMotion } from '../motion'

const SIZE = 16
const LOOP_MS = 3000
const REST = { spin: 0, drop: 0, core: 1.6 }

type Pose = typeof REST

function bezier(x1: number, y1: number, x2: number, y2: number): (x: number) => number {
  const at = (a: number, b: number, t: number) => 3 * a * t * (1 - t) ** 2 + 3 * b * t ** 2 * (1 - t) + t ** 3
  return (x) => {
    let low = 0
    let high = 1
    for (let step = 0; step < 24; step++) {
      const mid = (low + high) / 2
      if (at(x1, x2, mid) < x) low = mid
      else high = mid
    }
    return at(y1, y2, (low + high) / 2)
  }
}

function keyframes(stops: [number, number][], ease: (x: number) => number): (progress: number) => number {
  return (progress) => {
    const next = stops.findIndex(([offset]) => offset >= progress)
    if (next <= 0) return stops[Math.max(next, 0)][1]
    const [from, a] = stops[next - 1]
    const [to, b] = stops[next]
    return a + (b - a) * ease((progress - from) / (to - from))
  }
}

const drop = keyframes([[0, -7.3], [0.18, -7.3], [0.45, 0], [0.58, 0], [0.76, -8.2], [0.86, -7.3], [1, -7.3]], bezier(0.5, 0, 0.3, 1))
const core = keyframes([[0, 0], [0.18, 0], [0.4, 1], [0.5, 1.5], [0.62, 1], [0.76, 0], [1, 0]], bezier(0.42, 0, 0.58, 1))

function loopPose(elapsed: number): Pose {
  const progress = (elapsed % LOOP_MS) / LOOP_MS
  return { spin: 120 * progress, drop: drop(progress), core: core(progress) }
}

export function ThinkingGlyph({ thinking, color }: { thinking: boolean; color: string }) {
  const filter = `thinking-glyph-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`
  const edge = Math.min(20, Math.max(4, (SIZE * PixelRatio.get()) / 4))
  const reduced = useReducedMotion()
  const [pose, setPose] = useState<Pose>(() => (thinking ? loopPose(0) : REST))
  useEffect(() => {
    if (reduced || !thinking) return
    let frame = 0
    let origin: number | null = null
    const tick = (now: number) => {
      origin ??= now
      setPose(loopPose(now - origin))
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [thinking, reduced])

  const shown = reduced ? REST : pose
  return (
    <Svg width={SIZE} height={SIZE} viewBox="0 0 24 24">
      <Defs>
        <Filter id={filter} x="-50%" y="-50%" width="200%" height="200%">
          <FeGaussianBlur in="SourceGraphic" stdDeviation={1.15} result="blur" />
          <FeColorMatrix in="blur" type="matrix" values={`1 0 0 0 0  0 1 0 0 0  0 0 1 0 0  0 0 0 ${edge} ${-0.4 * edge}`} />
        </Filter>
      </Defs>
      <G filter={`url(#${filter})`}>
        <G transform={`rotate(${shown.spin} 12 12)`}>
          {[0, 120, 240].map((angle) => (
            <G key={angle} transform={`rotate(${angle} 12 12)`}>
              <Circle cx={12} cy={12 + shown.drop} r={3.2} fill={color} />
            </G>
          ))}
        </G>
        <Circle cx={12} cy={12} r={2.6 * shown.core} fill={color} />
      </G>
    </Svg>
  )
}

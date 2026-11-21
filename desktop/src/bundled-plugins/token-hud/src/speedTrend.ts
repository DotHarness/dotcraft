import { useEffect, useRef, useState } from 'react'

export type SpeedTrend = 'up' | 'down'

const SPEED_TREND_THRESHOLD = 0.08
const SPEED_TREND_VISIBLE_MS = 1800
const COUNT_MS = 520

function speedTrend(previous: number | null, next: number | null): SpeedTrend | null {
  if (previous === null || next === null) return null
  const change = (next - previous) / previous
  if (Math.abs(change) < SPEED_TREND_THRESHOLD) return null
  return change > 0 ? 'up' : 'down'
}

function prefersReducedMotion(): boolean {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

export function useSpeedTrend(value: number | null, waitingForSample: boolean): { trend: SpeedTrend | null; key: number } {
  const previous = useRef<number | null>(null)
  const [cue, setCue] = useState<{ trend: SpeedTrend | null; key: number }>({ trend: null, key: 0 })

  useEffect(() => {
    if (waitingForSample) previous.current = null
  }, [waitingForSample])

  useEffect(() => {
    if (value === null) return
    const trend = speedTrend(previous.current, value)
    previous.current = value
    setCue((current) => ({ trend, key: current.key + 1 }))
    if (trend === null) return
    const timer = globalThis.setTimeout(() => setCue((current) => ({ ...current, trend: null })), SPEED_TREND_VISIBLE_MS)
    return () => globalThis.clearTimeout(timer)
  }, [value])

  return cue
}

export function useCountedValue(target: number | null): number | null {
  const [shown, setShown] = useState(target)
  const current = useRef(target)

  useEffect(() => {
    if (target === null || current.current === null || prefersReducedMotion()) {
      current.current = target
      setShown(target)
      return
    }
    const from = current.current
    const started = performance.now()
    let frame = 0
    const step = (now: number): void => {
      const progress = Math.min(1, (now - started) / COUNT_MS)
      const next = from + (target - from) * (1 - Math.pow(1 - progress, 3))
      current.current = next
      setShown(next)
      if (progress < 1) frame = requestAnimationFrame(step)
    }
    frame = requestAnimationFrame(step)
    return () => cancelAnimationFrame(frame)
  }, [target])

  return shown
}

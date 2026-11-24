import { useEffect, useRef, useState } from 'react'

export type SpeedTrend = 'up' | 'down'

export interface SpeedComparison {
  readonly previous: number
  readonly next: number
}

const SPEED_TREND_THRESHOLD = 0.08
const SPEED_TREND_VISIBLE_MS = 3000
const COUNT_MS = 520

function speedTrend({ previous, next }: SpeedComparison): SpeedTrend | null {
  const change = (next - previous) / previous
  if (Math.abs(change) < SPEED_TREND_THRESHOLD) return null
  return change > 0 ? 'up' : 'down'
}

function prefersReducedMotion(): boolean {
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

export function useSpeedTrend(comparison: SpeedComparison | null): { trend: SpeedTrend | null; key: number } {
  const [cue, setCue] = useState<{ comparison: SpeedComparison; trend: SpeedTrend | null; key: number } | null>(null)

  useEffect(() => {
    if (comparison === null) return
    const trend = speedTrend(comparison)
    if (trend === null) return
    setCue((current) => ({ comparison, trend, key: (current?.key ?? 0) + 1 }))
    const timer = globalThis.setTimeout(() => setCue((current) => current && { ...current, trend: null }), SPEED_TREND_VISIBLE_MS)
    return () => globalThis.clearTimeout(timer)
  }, [comparison])

  return { trend: cue?.comparison === comparison ? cue.trend : null, key: cue?.key ?? 0 }
}

export function useCountedValue(target: number | null, scope: string | null): number | null {
  const [shown, setShown] = useState({ scope, value: target })
  const current = useRef(shown)

  useEffect(() => {
    const show = (value: number | null): void => {
      current.current = { scope, value }
      setShown(current.current)
    }
    const from = current.current.value
    const scopeChanged = current.current.scope !== scope
    if (!scopeChanged && from === target) return
    if (scopeChanged || target === null || from === null || prefersReducedMotion()) {
      show(target)
      return
    }
    const started = performance.now()
    let frame = 0
    const step = (now: number): void => {
      const progress = Math.min(1, (now - started) / COUNT_MS)
      const next = from + (target - from) * (1 - Math.pow(1 - progress, 3))
      show(next)
      if (progress < 1) frame = requestAnimationFrame(step)
    }
    frame = requestAnimationFrame(step)
    return () => cancelAnimationFrame(frame)
  }, [scope, target])

  return shown.scope === scope ? shown.value : target
}

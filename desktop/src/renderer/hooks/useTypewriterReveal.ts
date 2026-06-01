import { useEffect, useMemo, useRef, useState } from 'react'
import {
  advanceReveal,
  codePointLength,
  REVEAL_COMMIT_INTERVAL_MS,
  sliceByCodePoints
} from '../utils/typewriterReveal'

function prefersReducedMotion(): boolean {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

/**
 * Steady-cadence "typewriter" reveal for streaming assistant text.
 *
 * Returns a growing prefix of `targetText` that advances at a stable
 * characters-per-second rate, decoupled from the bursty arrival of network
 * deltas. The cursor only ever chases `targetText`; it never rewrites
 * already-shown characters, so the live Markdown render stays intact.
 *
 * When `enabled` is false (finalized message) or the user prefers reduced
 * motion, the full text is returned immediately with no animation.
 */
export function useTypewriterReveal(targetText: string, enabled: boolean): string {
  // Read once on mount: reduced-motion is rarely toggled mid-stream.
  const reducedMotion = useMemo(() => prefersReducedMotion(), [])
  const total = useMemo(() => codePointLength(targetText), [targetText])
  const animate = enabled && !reducedMotion

  // Reveal whatever text is already present at mount instantly (avoids an empty
  // flash and keeps non-streaming / SSR-free environments correct); only text
  // that arrives after mount types out.
  const [revealed, setRevealed] = useState(total)
  const revealedRef = useRef(total)
  const rafRef = useRef<number | null>(null)
  const lastTsRef = useRef<number | null>(null)
  const lastCommitRef = useRef(0)

  useEffect(() => {
    const cancel = (): void => {
      if (rafRef.current != null) {
        cancelAnimationFrame(rafRef.current)
        rafRef.current = null
      }
      lastTsRef.current = null
    }

    if (!animate) {
      // Finalized / reduced-motion: show everything, stop any running loop.
      cancel()
      revealedRef.current = total
      setRevealed(total)
      return cancel
    }

    // Target may have been replaced (retry/rollback) with shorter text.
    if (revealedRef.current > total) {
      revealedRef.current = total
      setRevealed(total)
    }
    // Already caught up: idle until `total` grows and re-runs this effect.
    if (revealedRef.current >= total) return cancel

    lastTsRef.current = null

    const step = (ts: number): void => {
      if (lastTsRef.current == null) {
        lastTsRef.current = ts
        lastCommitRef.current = ts
      }
      const dtSeconds = (ts - lastTsRef.current) / 1000
      lastTsRef.current = ts

      const next = advanceReveal(revealedRef.current, total, dtSeconds)
      revealedRef.current = next

      const reachedEnd = next >= total
      if (reachedEnd || ts - lastCommitRef.current >= REVEAL_COMMIT_INTERVAL_MS) {
        lastCommitRef.current = ts
        setRevealed(Math.floor(next))
      }

      if (reachedEnd) {
        rafRef.current = null
        lastTsRef.current = null
      } else {
        rafRef.current = requestAnimationFrame(step)
      }
    }

    rafRef.current = requestAnimationFrame(step)
    return cancel
  }, [animate, total])

  if (!animate) return targetText
  return sliceByCodePoints(targetText, revealed)
}

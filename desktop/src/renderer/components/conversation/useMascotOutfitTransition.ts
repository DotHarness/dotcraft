import { useEffect, useRef, useState } from 'react'
import { MASCOT_OUTFIT_DURATION_MS, MASCOT_OUTFIT_SWAP_MS } from '@dotcraft/avatar/react'
import { prefersReducedMotion } from '../../utils/appearance'

export type MascotOutfitPhase = 'in' | 'out' | null

/** A user toggle arms the next outfit change. Loads and thread switches swap it at once. */
const ARMED_MS = 1500

export function useMascotOutfitTransition<T>(outfit: T | undefined, signal: number): {
  outfit: T | undefined
  phase: MascotOutfitPhase
  spin: number
} {
  const [rendered, setRendered] = useState(outfit)
  const [phase, setPhase] = useState<MascotOutfitPhase>(null)
  const [spin, setSpin] = useState(0)
  const renderedRef = useRef(outfit)
  const lastSignal = useRef(signal)
  const armedAt = useRef(Number.NEGATIVE_INFINITY)

  useEffect(() => {
    if (signal === lastSignal.current) return
    lastSignal.current = signal
    armedAt.current = performance.now()
  }, [signal])

  useEffect(() => {
    if (outfit === renderedRef.current) return
    const animate = performance.now() - armedAt.current < ARMED_MS && !prefersReducedMotion()
    armedAt.current = Number.NEGATIVE_INFINITY
    const commit = (): void => {
      renderedRef.current = outfit
      setRendered(outfit)
    }
    if (!animate) {
      commit()
      setPhase(null)
      return undefined
    }
    const dressing = outfit !== undefined
    setSpin((value) => value + 1)
    setPhase(dressing ? null : 'out')
    const swap = window.setTimeout(() => {
      commit()
      setPhase(dressing ? 'in' : null)
    }, MASCOT_OUTFIT_SWAP_MS)
    const done = window.setTimeout(() => setPhase(null), MASCOT_OUTFIT_DURATION_MS)
    return () => {
      window.clearTimeout(swap)
      window.clearTimeout(done)
      commit()
      setPhase(null)
    }
  }, [outfit])

  return { outfit: rendered, phase, spin }
}

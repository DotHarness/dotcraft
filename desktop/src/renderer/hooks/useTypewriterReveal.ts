import { useLayoutEffect, useMemo, useState, useSyncExternalStore } from 'react'
import { TypewriterBuffer } from '../utils/typewriterReveal'

function prefersReducedMotion(): boolean {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false
  return window.matchMedia('(prefers-reduced-motion: reduce)').matches
}

export function useTypewriterReveal(targetText: string, enabled: boolean): string {
  const reducedMotion = useMemo(() => prefersReducedMotion(), [])
  const animate = enabled && !reducedMotion
  const [buffer] = useState(() => new TypewriterBuffer(targetText, animate))
  const snapshot = useSyncExternalStore(buffer.subscribe, buffer.getSnapshot, buffer.getSnapshot)

  useLayoutEffect(() => () => buffer.destroy(), [buffer])
  useLayoutEffect(() => {
    buffer.update(targetText, animate)
  }, [buffer, targetText, animate])

  return animate && targetText.startsWith(snapshot.source) ? snapshot.text : targetText
}

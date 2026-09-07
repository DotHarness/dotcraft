import { useEffect, useState } from 'react'
import { prefersReducedMotion } from './constants.js'
export function useComposerMotion(mode: 'system' | 'on' | 'off') {
  const [reduced, setReduced] = useState(prefersReducedMotion)
  useEffect(() => {
    const update = () => setReduced(prefersReducedMotion())
    const media = window.matchMedia?.('(prefers-reduced-motion: reduce)')
    media?.addEventListener?.('change', update)
    const observer = new MutationObserver(update)
    observer.observe(document.documentElement, { attributes: true, attributeFilter: ['data-reduce-motion'] })
    update()
    return () => { media?.removeEventListener?.('change', update); observer.disconnect() }
  }, [])
  return mode === 'on' || (mode === 'system' && !reduced)
}

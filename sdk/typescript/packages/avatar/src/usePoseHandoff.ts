import { useCallback, useLayoutEffect, useRef, type RefObject } from 'react'

const HANDOFF_SELECTOR = '.dca-body-motion, .dca-action-jelly, .dca-part-arm-l-w, .dca-part-arm-l-b, .dca-part-arm-l, .dca-part-arm-r-w, .dca-part-arm-r-b'
const BLEND_MS = 240
const SNAPSHOT_TTL_MS = 100

function transformOf(element: Element) {
  const value = getComputedStyle(element).transform
  return value === 'none' ? 'matrix(1, 0, 0, 1, 0, 0)' : value
}

function hasTransformTransition(element: Element) {
  return element.getAnimations().some(animation => (animation as CSSTransition).transitionProperty === 'transform')
}

// No transition starts while an animation owns transform, so layers that would jump on a pose change
// blend from their captured value into whatever drives them next.
export function usePoseHandoff(ref: RefObject<Element | null>, enabled: boolean) {
  const snapshot = useRef<{ at: number; values: Map<Element, string> } | null>(null)
  const capture = useCallback(() => {
    const root = ref.current
    if (!enabled || !root || typeof root.animate !== 'function') return
    const values = new Map<Element, string>()
    root.querySelectorAll(HANDOFF_SELECTOR).forEach(element => values.set(element, transformOf(element)))
    snapshot.current = { at: performance.now(), values }
  }, [ref, enabled])
  useLayoutEffect(() => {
    const taken = snapshot.current
    if (!taken) return
    snapshot.current = null
    if (!enabled || performance.now() - taken.at > SNAPSHOT_TTL_MS) return
    for (const [element, from] of taken.values) {
      if (hasTransformTransition(element) || transformOf(element) === from) continue
      element.animate([{ transform: from, offset: 0 }], { duration: BLEND_MS, easing: 'cubic-bezier(.22, .7, .3, 1)' })
    }
  })
  return capture
}

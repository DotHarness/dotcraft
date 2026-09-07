import { useEffect, useRef, type RefObject } from 'react'

const eventAnimations = new Set(['dca-celebrate', 'dca-nod', 'dca-action-wave-arm', 'dca-action-wave-lean'])
export function useEventReplay(ref: RefObject<Element | null>, sequence: number, enabled: boolean, paused: boolean) {
  const previous = useRef(sequence)
  useEffect(() => {
    if (!enabled || paused || previous.current === sequence) return
    previous.current = sequence
    ref.current?.getAnimations?.({ subtree: true }).forEach(animation => {
      if (eventAnimations.has((animation as CSSAnimation).animationName)) animation.currentTime = 0
    })
  }, [ref, sequence, enabled, paused])
}

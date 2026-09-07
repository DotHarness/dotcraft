import { useEffect, useState, type RefObject } from 'react'
import type { MotionMode } from './characters.js'

export function useMotionEnvironment(mode: MotionMode, observe = true) {
  const [reduced, setReduced] = useState(true)
  useEffect(() => {
    if (!observe || mode === 'off' || typeof window.matchMedia !== 'function') return
    const media = window.matchMedia('(prefers-reduced-motion: reduce)')
    const update = () => setReduced(media.matches)
    update()
    if (typeof media.addEventListener === 'function') {
      media.addEventListener('change', update)
      return () => media.removeEventListener('change', update)
    }
    media.addListener?.(update)
    return () => media.removeListener?.(update)
  }, [mode, observe])
  return observe && (mode === 'on' || (mode === 'system' && !reduced))
}

export function useOnscreen(ref: RefObject<Element | null>, observe = true) {
  const [visible, setVisible] = useState(true)
  useEffect(() => {
    if (!observe) return
    let intersects = true
    const update = () => setVisible(intersects && !document.hidden)
    const observer = typeof IntersectionObserver === 'undefined' ? undefined
      : new IntersectionObserver(([entry]) => { intersects = entry.isIntersecting; update() })
    if (ref.current) observer?.observe(ref.current)
    document.addEventListener('visibilitychange', update)
    update()
    return () => { observer?.disconnect(); document.removeEventListener('visibilitychange', update) }
  }, [ref, observe])
  return visible
}

export function useTransitionPause(ref: RefObject<Element | null>, paused: boolean) {
  useEffect(() => {
    if (!paused) return
    // CSS play-state freezes keyframes; Web Animations also freezes in-flight arm transitions.
    const transitions = ref.current?.getAnimations?.({ subtree: true })
      .filter(animation => typeof (animation as CSSTransition).transitionProperty === 'string') ?? []
    transitions.forEach(animation => animation.pause())
    return () => {
      transitions.forEach(animation => {
        if (ref.current?.isConnected && animation.playState === 'paused') animation.play()
      })
    }
  }, [ref, paused])
}

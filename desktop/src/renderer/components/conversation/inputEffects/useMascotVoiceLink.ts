import { useEffect, useState, type RefObject } from 'react'
import { prefersReducedMotion } from '../../../utils/appearance'
import { useVoiceStore } from '../../../voice/voiceStore'
import styles from './MascotVoiceBeam.module.css'

/** The ribbon's trails converge on the control near the end of its 700 ms exit. */
const BEAM_DELAY_MS = 540
const BEAM_MS = 340
const BEAM_COLORS = ['#51f9d5', '#44b2ff', '#9c86fa', '#ff68cf']

function centerIn(root: DOMRect, scale: number, element: Element): { x: number; y: number } | null {
  const rect = element.getBoundingClientRect()
  if (rect.width <= 0 || rect.height <= 0) return null
  return { x: (rect.left + rect.width / 2 - root.left) / scale, y: (rect.top + rect.height / 2 - root.top) / scale }
}

function fireBeam(root: HTMLElement, slot: 'primary' | 'voice'): boolean {
  const button = root.querySelector(`button[data-composer-exit-target="${slot}"]`)
  const mascot = root.querySelector('[data-composer-avatar-pose] .composer-mascot-character-stage')
  if (!button || !mascot) return false
  const bounds = root.getBoundingClientRect()
  const scale = root.offsetWidth > 0 ? bounds.width / root.offsetWidth : 1
  const from = centerIn(bounds, scale, button)
  const to = centerIn(bounds, scale, mascot)
  if (!from || !to) return false
  const bend = { x: (from.x + to.x) / 2 - 26, y: (from.y + to.y) / 2 + 6 }
  const frames: Keyframe[] = Array.from({ length: 13 }, (_, index) => {
    const t = index / 12
    const u = 1 - t
    const x = u * u * from.x + 2 * u * t * bend.x + t * t * to.x
    const y = u * u * from.y + 2 * u * t * bend.y + t * t * to.y
    return { transform: `translate(${x}px, ${y}px) scale(${1.1 - t * 0.5})`, opacity: t < 0.9 ? 1 : 0, offset: t }
  })
  BEAM_COLORS.forEach((color, index) => {
    const dot = document.createElement('span')
    dot.className = styles.dot
    dot.style.setProperty('--beam-color', color)
    root.appendChild(dot)
    const animation = dot.animate(frames, { duration: BEAM_MS, delay: index * 34, easing: 'cubic-bezier(0.55, 0, 0.8, 0.6)', fill: 'both' })
    animation.onfinish = () => dot.remove()
  })
  return true
}

export function useMascotVoiceLink(root: RefObject<HTMLElement | null>, origin: string | undefined, enabled: boolean): {
  listening: boolean
  catchSignal: number
} {
  const listening = useVoiceStore((state) => enabled && origin !== undefined && state.recording?.threadId === origin)
  const [catchSignal, setCatchSignal] = useState(0)

  useEffect(() => {
    const element = root.current?.querySelector<HTMLElement>('[data-composer-avatar-pose]')
    if (!listening || !element) return undefined
    let frame = 0
    let level = 0
    const tick = (): void => {
      const raw = useVoiceStore.getState().recording?.level ?? 0
      level += (Math.sqrt(raw) - level) * 0.35
      element.style.setProperty('--mascot-voice-level', level.toFixed(3))
      frame = window.requestAnimationFrame(tick)
    }
    frame = window.requestAnimationFrame(tick)
    return () => {
      window.cancelAnimationFrame(frame)
      element.style.removeProperty('--mascot-voice-level')
    }
  }, [listening, root])

  useEffect(() => {
    if (!enabled || origin === undefined) return undefined
    const timers: number[] = []
    const unsubscribe = useVoiceStore.subscribe((next, previous) => {
      if (previous.recording?.threadId !== origin || next.recording?.threadId === origin) return
      const finalizing = next.finalizing
      if (finalizing?.threadId !== origin || previous.finalizing === finalizing || prefersReducedMotion()) return
      timers.push(window.setTimeout(() => {
        const element = root.current
        if (!element || !fireBeam(element, finalizing.intent === 'send' ? 'primary' : 'voice')) return
        timers.push(window.setTimeout(() => setCatchSignal((value) => value + 1), BEAM_MS))
      }, BEAM_DELAY_MS))
    })
    return () => {
      unsubscribe()
      timers.forEach((timer) => window.clearTimeout(timer))
    }
  }, [enabled, origin, root])

  return { listening, catchSignal }
}

import { useEffect, useRef, type RefObject } from 'react'
import type { AvatarGesture } from './characters.js'

const durations = { blink: 520, 'look-left': 1100, 'look-right': 1100, 'antenna-bob': 720 }
function curve(points: readonly (readonly [number, number])[], progress: number) {
  const next = points.findIndex(([at]) => at >= progress)
  if (next <= 0) return points[Math.max(0, next)][1]
  const [from, a] = points[next - 1], [to, b] = points[next]
  const t = (progress - from) / (to - from)
  return a + (b - a) * t * t * (3 - 2 * t)
}
export function useGesture(ref: RefObject<Element | null>, gesture: AvatarGesture | undefined, sequence: number,
  enabled: boolean, paused: boolean, onComplete?: (sequence: number) => void) {
  const clock = useRef(0)
  const complete = useRef(onComplete)
  const delivered = useRef(false)
  complete.current = onComplete
  function paint(elapsed: number) {
    const root = ref.current
    if (!root) return
    const duration = gesture ? durations[gesture] : 1
    const t = Math.min(1, elapsed / duration)
    const active = enabled && !!gesture && t < 1
    const eye = active && gesture === 'blink' && elapsed < 180 ? curve([[0,1],[.35,.08],[.62,.08],[1,1]], elapsed / 180) : 1
    root.querySelectorAll<SVGElement>('.dca-part-eyes').forEach(e => { e.style.transform = `scaleY(${eye})` })
    root.querySelectorAll<SVGElement>('.dca-part-caret').forEach(e => { e.style.opacity = active && gesture === 'blink' && Math.floor(elapsed / 130) % 2 ? '0' : '' })
    const hold = t > .22 && t < .78 ? 1 : t >= .78 ? Math.sin(Math.PI / 2 * (1 - t) / .22) : Math.sin(Math.PI / 2 * t / .22)
    const x = active && gesture?.startsWith('look-') ? (gesture === 'look-left' ? -34 : 34) * hold : 0
    root.querySelector('.dca-face-motion')?.setAttribute('transform', `translate(${x} 0)`)
    const bob = active && gesture === 'antenna-bob' ? curve([[0,0],[.3,-52],[.55,12],[.75,-16],[1,0]],t) : 0
    root.querySelectorAll<SVGElement>('.dca-part-antenna-w,.dca-part-glow,.dca-part-light').forEach(e => { e.style.transform = `translateY(${bob}px)` })
  }
  useEffect(() => { clock.current = 0; delivered.current = false; paint(0) }, [gesture, sequence, enabled])
  useEffect(() => {
    if (!gesture || delivered.current) return
    const finish = () => { delivered.current = true; complete.current?.(sequence) }
    if (!enabled) { paint(durations[gesture]); finish(); return }
    if (paused) return
    let last: number | undefined
    let frame = 0
    const tick = (now: number) => {
      clock.current += last === undefined ? 0 : now - last
      last = now
      paint(clock.current)
      if (clock.current < durations[gesture]) frame = requestAnimationFrame(tick)
      else finish()
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [gesture, sequence, enabled, paused])
}

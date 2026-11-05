import { useEffect, useRef, type RefObject } from 'react'

const WINDOW_RANGE = 600
const REPAINT_STEP = .15

export interface AvatarGazeOptions {
  enabled: boolean
  within?: HTMLElement | null
  reach?: number
}

export function useAvatarGaze(ref: RefObject<HTMLElement | null>, { enabled, within, reach = 160 }: AvatarGazeOptions): void {
  const pointer = useRef<{ x: number; y: number } | null>(null)
  useEffect(() => {
    const element = ref.current
    const area = within === undefined ? window : within
    if (!element || !area || !matchMedia('(hover: hover) and (pointer: fine)').matches || matchMedia('(prefers-reduced-motion: reduce)').matches) return
    let frame = 0
    let x = 0, y = 0, lastX = -Infinity, lastY = -Infinity
    const release = (): void => {
      cancelAnimationFrame(frame)
      frame = 0
      element.style.removeProperty('--dca-gaze-x')
      element.style.removeProperty('--dca-gaze-y')
      lastX = lastY = -Infinity
    }
    const paint = (): void => {
      frame = 0
      lastX = x; lastY = y
      const rect = element.getBoundingClientRect()
      const dx = x - rect.left - rect.width / 2
      const dy = y - rect.top - rect.height / 2
      const distance = Math.hypot(dx, dy)
      if (within === undefined && distance > WINDOW_RANGE) { release(); return }
      const scale = distance > 0 ? Math.min(1, distance / reach) / distance : 0
      element.style.setProperty('--dca-gaze-x', (dx * scale).toFixed(3))
      element.style.setProperty('--dca-gaze-y', (dy * scale).toFixed(3))
    }
    const move = (event: Event): void => {
      const { clientX, clientY } = event as PointerEvent
      x = clientX; y = clientY
      pointer.current = { x, y }
      if (enabled && !frame && Math.hypot(x - lastX, y - lastY) >= reach * REPAINT_STEP) frame = requestAnimationFrame(paint)
    }
    const leave = (): void => { pointer.current = null; release() }
    if (enabled && pointer.current) {
      x = pointer.current.x; y = pointer.current.y
      frame = requestAnimationFrame(paint)
    }
    const leaves = within ?? document
    area.addEventListener('pointermove', move, { passive: true })
    leaves.addEventListener('pointerleave', leave)
    return () => { release(); area.removeEventListener('pointermove', move); leaves.removeEventListener('pointerleave', leave) }
  }, [ref, enabled, within, reach])
}

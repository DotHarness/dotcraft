import { useEffect, useRef, type RefObject } from 'react'

export function usePetGaze(ref: RefObject<HTMLSpanElement | null>, enabled: boolean): void {
  const pointer = useRef<{ x: number; y: number } | null>(null)
  useEffect(() => {
    const element = ref.current
    if (!matchMedia('(hover: hover) and (pointer: fine)').matches) return
    let frame = 0
    let x = 0, y = 0, lastX = -Infinity, lastY = -Infinity
    const release = (): void => {
      cancelAnimationFrame(frame)
      frame = 0
      element?.style.removeProperty('--pet-gaze-x')
      element?.style.removeProperty('--pet-gaze-y')
      lastX = lastY = -Infinity
    }
    const paint = (): void => {
      frame = 0
      if (!element || !enabled) return
      lastX = x; lastY = y
      const rect = element.getBoundingClientRect()
      const dx = x - rect.left - rect.width / 2
      const dy = y - rect.top - rect.height / 2
      const distance = Math.hypot(dx, dy)
      if (distance > 600) { release(); return }
      // Match the documentation mascot's bounded gaze, in the Avatar's SVG units.
      const scale = distance > 0 ? Math.min(1, distance / 160) / distance : 0
      element.style.setProperty('--pet-gaze-x', (dx * scale).toFixed(3))
      element.style.setProperty('--pet-gaze-y', (dy * scale).toFixed(3))
    }
    const move = (event: PointerEvent): void => {
      x = event.clientX; y = event.clientY
      pointer.current = { x, y }
      if (!element || !enabled) return
      if (Math.hypot(x - lastX, y - lastY) >= 24 && !frame) frame = requestAnimationFrame(paint)
    }
    const leave = (): void => { pointer.current = null; release() }
    if (enabled && element && pointer.current) {
      x = pointer.current.x; y = pointer.current.y
      frame = requestAnimationFrame(paint)
    }
    window.addEventListener('pointermove', move, { passive: true })
    document.addEventListener('pointerleave', leave)
    return () => { release(); window.removeEventListener('pointermove', move); document.removeEventListener('pointerleave', leave) }
  }, [ref, enabled])
}

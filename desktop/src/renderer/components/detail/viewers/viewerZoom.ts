import { useEffect, type RefObject } from 'react'

export const ZOOM_PRESETS = [0.25, 0.5, 1, 1.5, 2]
const MIN_ZOOM = 0.1
const MAX_ZOOM = 4

export function clampZoom(value: number): number {
  return Math.min(MAX_ZOOM, Math.max(MIN_ZOOM, Number.isFinite(value) ? value : 1))
}

/** Trackpad pinch arrives as a ctrl wheel event, so it zooms through the same path. */
function wheelZoom(current: number, deltaY: number): number {
  return clampZoom(current * Math.exp(-deltaY * 0.01))
}

export function useWheelZoom(target: RefObject<HTMLElement | null>, onZoom: (update: (current: number) => number) => void): void {
  useEffect(() => {
    const element = target.current
    if (!element) return
    const onWheel = (event: WheelEvent) => {
      if (!event.ctrlKey && !event.metaKey) return
      event.preventDefault()
      onZoom((current) => wheelZoom(current, event.deltaY))
    }
    element.addEventListener('wheel', onWheel, { passive: false })
    return () => element.removeEventListener('wheel', onWheel)
  }, [target, onZoom])
}

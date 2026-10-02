export const IMAGE_ZOOM_PRESETS = [25, 50, 100, 150, 200] as const
const MIN_IMAGE_ZOOM = 10
const MAX_IMAGE_ZOOM = 400
const BACKDROP_CLICK_SLOP_PX = 5
const WHEEL_ZOOM_SENSITIVITY = 0.01
const KEYBOARD_ZOOM_FACTOR = 1.25

function clampImageZoom(percent: number): number {
  return Math.min(MAX_IMAGE_ZOOM, Math.max(MIN_IMAGE_ZOOM, percent))
}

export function wheelImageZoom(percent: number, deltaY: number): number {
  return clampImageZoom(Math.round(percent * Math.exp(-deltaY * WHEEL_ZOOM_SENSITIVITY)))
}

export function pinchImageZoom(initialDistance: number, initialPercent: number, distance: number): number {
  return clampImageZoom(Math.round((distance / initialDistance) * initialPercent))
}

export function stepImageZoom(percent: number, direction: 1 | -1): number {
  return clampImageZoom(Math.round(direction > 0 ? percent * KEYBOARD_ZOOM_FACTOR : percent / KEYBOARD_ZOOM_FACTOR))
}

export function movedBeyondClickSlop(
  start: { x: number; y: number },
  end: { x: number; y: number }
): boolean {
  return Math.hypot(end.x - start.x, end.y - start.y) > BACKDROP_CLICK_SLOP_PX
}

export function stepGalleryIndex(index: number, delta: 1 | -1, total: number): number | null {
  const next = index + delta
  return next >= 0 && next < total ? next : null
}

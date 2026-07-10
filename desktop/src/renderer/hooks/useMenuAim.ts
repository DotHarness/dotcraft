import { useCallback, useEffect, useRef, type RefObject } from 'react'

export type MenuAimSide = 'left' | 'right'

export interface MenuAimPoint {
  clientX: number
  clientY: number
}

interface UseMenuAimOptions {
  submenuRef: RefObject<HTMLElement | null>
  side: MenuAimSide
  delayMs?: number
  bufferPx?: number
}

interface MenuAimController {
  track: (point: MenuAimPoint) => void
  guard: (point: MenuAimPoint, action: () => void) => void
  cancel: () => void
}

interface Point {
  x: number
  y: number
}

const DEFAULT_DELAY_MS = 280
const DEFAULT_BUFFER_PX = 6
const REVERSE_JITTER_TOLERANCE_PX = 4

export function useMenuAim({
  submenuRef,
  side,
  delayMs = DEFAULT_DELAY_MS,
  bufferPx = DEFAULT_BUFFER_PX
}: UseMenuAimOptions): MenuAimController {
  const anchorRef = useRef<Point | null>(null)
  const timerRef = useRef<number | null>(null)

  const clearTimer = useCallback((): void => {
    if (timerRef.current !== null) window.clearTimeout(timerRef.current)
    timerRef.current = null
  }, [])

  const cancel = useCallback((): void => {
    clearTimer()
    anchorRef.current = null
  }, [clearTimer])

  const track = useCallback((point: MenuAimPoint): void => {
    clearTimer()
    anchorRef.current = toPoint(point)
  }, [clearTimer])

  const guard = useCallback((point: MenuAimPoint, action: () => void): void => {
    const anchor = anchorRef.current
    const submenu = submenuRef.current
    const submenuRect = submenu?.getBoundingClientRect()
    const currentPoint = toPoint(point)

    if (!anchor || !submenu || !submenuRect || !isInsidePredictionCone(currentPoint, anchor, submenuRect, side, bufferPx)) {
      cancel()
      action()
      return
    }

    clearTimer()
    timerRef.current = window.setTimeout(() => {
      timerRef.current = null
      anchorRef.current = null
      if (submenuRef.current?.matches(':hover')) return
      action()
    }, delayMs)
  }, [bufferPx, cancel, clearTimer, delayMs, side, submenuRef])

  useEffect(() => cancel, [cancel])

  return { track, guard, cancel }
}

function toPoint(point: MenuAimPoint): Point {
  return { x: point.clientX, y: point.clientY }
}

function isInsidePredictionCone(
  point: Point,
  anchor: Point,
  submenuRect: DOMRect,
  side: MenuAimSide,
  bufferPx: number
): boolean {
  const submenuEdgeX = side === 'left' ? submenuRect.right : submenuRect.left
  const toleratedEdgeX = side === 'left'
    ? submenuEdgeX - REVERSE_JITTER_TOLERANCE_PX
    : submenuEdgeX + REVERSE_JITTER_TOLERANCE_PX
  const isMovingTowardSubmenu = side === 'left'
    ? point.x <= anchor.x && point.x >= toleratedEdgeX
    : point.x >= anchor.x && point.x <= toleratedEdgeX
  if (!isMovingTowardSubmenu) return false

  return isPointInTriangle(
    point,
    anchor,
    { x: toleratedEdgeX, y: submenuRect.top - bufferPx },
    { x: toleratedEdgeX, y: submenuRect.bottom + bufferPx }
  )
}

function isPointInTriangle(point: Point, a: Point, b: Point, c: Point): boolean {
  const sign = (p1: Point, p2: Point, p3: Point): number =>
    (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y)
  const d1 = sign(point, a, b)
  const d2 = sign(point, b, c)
  const d3 = sign(point, c, a)
  const hasNegative = d1 < 0 || d2 < 0 || d3 < 0
  const hasPositive = d1 > 0 || d2 > 0 || d3 > 0
  return !(hasNegative && hasPositive)
}

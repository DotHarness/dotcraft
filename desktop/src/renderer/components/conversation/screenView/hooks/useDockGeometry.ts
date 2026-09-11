import {
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type KeyboardEvent,
  type MouseEvent,
  type PointerEvent,
  type RefObject
} from 'react'
import type { ScreenViewDockPosition } from '../../../../../shared/screenView'
import {
  DOCK_MARGIN,
  DOCK_TOP_INSET,
  DOCK_WIDTH_MAX,
  clampDockPosition,
  clampDockWidth,
  type DockGeometry
} from '../../../../stores/screenViewStore'

const DRAG_DEAD_ZONE = 5
const KEYBOARD_STEP = 24
const ANCHOR_GAP = 10
const FALLBACK_ASPECT = 16 / 9

const ARROWS: Record<string, { x: number; y: number } | undefined> = {
  ArrowLeft: { x: -1, y: 0 },
  ArrowRight: { x: 1, y: 0 },
  ArrowUp: { x: 0, y: -1 },
  ArrowDown: { x: 0, y: 1 }
}

interface DockGeometryOptions {
  rootRef: RefObject<HTMLElement | null>
  anchorRef: RefObject<HTMLElement | null>
  width: number
  position: ScreenViewDockPosition | null
  commit(geometry: DockGeometry): void
}

export interface DockGeometryHandles extends DockGeometry {
  dragging: boolean
  onPointerDown(event: PointerEvent<HTMLElement>): void
  onPointerMove(event: PointerEvent<HTMLElement>): void
  onPointerUp(event: PointerEvent<HTMLElement>): void
  onKeyDown(event: KeyboardEvent<HTMLElement>): void
  onClickCapture(event: MouseEvent<HTMLElement>): void
  onGripPointerDown(event: PointerEvent<HTMLElement>): void
  onGripKeyDown(event: KeyboardEvent<HTMLElement>): void
}

function resized(origin: DockGeometry, dx: number): DockGeometry {
  const right = origin.position.x + origin.width
  const width = clampDockWidth(origin.width - dx, Math.min(DOCK_WIDTH_MAX, right - DOCK_MARGIN))
  return { width, position: { x: right - width, y: origin.position.y } }
}

export function useDockGeometry(options: DockGeometryOptions): DockGeometryHandles {
  const { rootRef, anchorRef, width, position, commit } = options
  const [viewport, setViewport] = useState(() => ({ width: window.innerWidth, height: window.innerHeight }))
  const [height, setHeight] = useState(0)
  const [anchor, setAnchor] = useState<{ right: number; bottom: number } | null>(null)
  const [draft, setDraft] = useState<DockGeometry | null>(null)
  const [dragging, setDragging] = useState(false)
  const draftRef = useRef<DockGeometry | null>(null)
  const dragRef = useRef<{
    pointerId: number
    x: number
    y: number
    origin: DockGeometry
    resize: boolean
    moved: boolean
  } | null>(null)
  const suppressClick = useRef(false)

  useEffect(() => {
    const measure = (): void => setViewport({ width: window.innerWidth, height: window.innerHeight })
    window.addEventListener('resize', measure)
    return () => window.removeEventListener('resize', measure)
  }, [])

  useEffect(() => {
    const node = rootRef.current
    if (!node) return
    const observer = new ResizeObserver(() => setHeight(node.offsetHeight))
    observer.observe(node)
    setHeight(node.offsetHeight)
    return () => observer.disconnect()
  }, [rootRef])

  useLayoutEffect(() => {
    const rect = anchorRef.current?.getBoundingClientRect()
    if (rect) setAnchor({ right: rect.right, bottom: rect.bottom })
  }, [anchorRef])

  const home: ScreenViewDockPosition = anchor
    ? { x: anchor.right - width, y: anchor.bottom + ANCHOR_GAP }
    : { x: viewport.width - width - DOCK_MARGIN, y: DOCK_TOP_INSET }
  const base = draft ?? { width, position: position ?? home }
  const size = { width: clampDockWidth(base.width), height: height || Math.round(base.width / FALLBACK_ASPECT) }
  const current: DockGeometry = {
    width: size.width,
    position: clampDockPosition(base.position, size, viewport)
  }

  const settle = (geometry: DockGeometry): void => {
    commit({
      width: geometry.width,
      position: clampDockPosition(geometry.position, { width: geometry.width, height: size.height }, viewport)
    })
  }

  const begin = (event: PointerEvent<HTMLElement>, resize: boolean): void => {
    dragRef.current = {
      pointerId: event.pointerId,
      x: event.clientX,
      y: event.clientY,
      origin: current,
      resize,
      moved: resize
    }
    if (!resize) return
    event.currentTarget.setPointerCapture(event.pointerId)
    setDragging(true)
  }

  useEffect(() => {
    if (!dragging) return
    const cancel = (event: globalThis.KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      event.preventDefault()
      dragRef.current = null
      draftRef.current = null
      setDraft(null)
      setDragging(false)
    }
    window.addEventListener('keydown', cancel)
    return () => window.removeEventListener('keydown', cancel)
  }, [dragging])

  return {
    ...current,
    dragging,

    onPointerDown(event) {
      if (event.button !== 0) return
      suppressClick.current = false
      begin(event, false)
    },

    onPointerMove(event) {
      const drag = dragRef.current
      if (!drag || drag.pointerId !== event.pointerId) return
      const dx = event.clientX - drag.x
      const dy = event.clientY - drag.y
      if (!drag.moved) {
        if (Math.hypot(dx, dy) < DRAG_DEAD_ZONE) return
        drag.moved = true
        event.currentTarget.setPointerCapture(event.pointerId)
        setDragging(true)
      }
      const next = drag.resize
        ? resized(drag.origin, dx)
        : { width: drag.origin.width, position: { x: drag.origin.position.x + dx, y: drag.origin.position.y + dy } }
      draftRef.current = next
      setDraft(next)
    },

    onPointerUp(event) {
      const drag = dragRef.current
      if (!drag || drag.pointerId !== event.pointerId) return
      dragRef.current = null
      setDragging(false)
      const settled = draftRef.current
      draftRef.current = null
      setDraft(null)
      if (!settled) return
      suppressClick.current = true
      settle(settled)
    },

    onKeyDown(event) {
      const arrow = ARROWS[event.key]
      if (!arrow) return
      event.preventDefault()
      settle({
        width: current.width,
        position: {
          x: current.position.x + arrow.x * KEYBOARD_STEP,
          y: current.position.y + arrow.y * KEYBOARD_STEP
        }
      })
    },

    onClickCapture(event) {
      if (!suppressClick.current) return
      suppressClick.current = false
      event.preventDefault()
      event.stopPropagation()
    },

    onGripPointerDown(event) {
      if (event.button !== 0) return
      event.preventDefault()
      event.stopPropagation()
      begin(event, true)
    },

    onGripKeyDown(event) {
      const step = event.key === 'ArrowLeft' ? KEYBOARD_STEP : event.key === 'ArrowRight' ? -KEYBOARD_STEP : 0
      if (step === 0) return
      event.preventDefault()
      event.stopPropagation()
      settle(resized(current, -step))
    }
  }
}

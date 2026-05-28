import { type CSSProperties, useCallback, useEffect, useRef, useState } from 'react'

interface DragHandleProps {
  onDrag: (delta: number) => void
  className?: string
  style?: CSSProperties
  onActiveChange?: (active: boolean) => void
  onDragStateChange?: (dragging: boolean) => void
}

/**
 * An invisible resize hit area.
 *
 * The owning layout paints the real panel border. This keeps the visible
 * divider attached to the surface it belongs to instead of drawing a second
 * line from the hit area.
 */
export function DragHandle({
  onDrag,
  className = '',
  style,
  onActiveChange,
  onDragStateChange
}: DragHandleProps): JSX.Element {
  const isDragging = useRef(false)
  const lastX = useRef(0)
  const hoveringRef = useRef(false)
  const draggingRef = useRef(false)
  const [hovering, setHovering] = useState(false)
  const [dragging, setDragging] = useState(false)
  const active = hovering || dragging

  const notifyActive = useCallback(
    (nextHovering = hoveringRef.current, nextDragging = draggingRef.current) => {
      onActiveChange?.(nextHovering || nextDragging)
    },
    [onActiveChange]
  )

  const updateHovering = useCallback(
    (nextHovering: boolean) => {
      hoveringRef.current = nextHovering
      setHovering(nextHovering)
      notifyActive(nextHovering, draggingRef.current)
    },
    [notifyActive]
  )

  const updateDragging = useCallback(
    (nextDragging: boolean) => {
      draggingRef.current = nextDragging
      isDragging.current = nextDragging
      setDragging(nextDragging)
      onDragStateChange?.(nextDragging)
      notifyActive(hoveringRef.current, nextDragging)
    },
    [notifyActive, onDragStateChange]
  )

  useEffect(() => {
    return () => {
      onActiveChange?.(false)
      onDragStateChange?.(false)
    }
  }, [onActiveChange, onDragStateChange])

  const handlePointerDown = useCallback(
    (e: React.PointerEvent) => {
      e.preventDefault()
      updateDragging(true)
      lastX.current = e.clientX

      function onPointerMove(event: PointerEvent): void {
        if (!isDragging.current) return
        const delta = event.clientX - lastX.current
        lastX.current = event.clientX
        onDrag(delta)
      }

      function onPointerUp(): void {
        updateDragging(false)
        document.removeEventListener('pointermove', onPointerMove)
        document.removeEventListener('pointerup', onPointerUp)
        document.removeEventListener('pointercancel', onPointerUp)
        document.body.style.cursor = ''
        document.body.style.userSelect = ''
      }

      document.addEventListener('pointermove', onPointerMove)
      document.addEventListener('pointerup', onPointerUp)
      document.addEventListener('pointercancel', onPointerUp)
      document.body.style.cursor = 'col-resize'
      document.body.style.userSelect = 'none'
    },
    [onDrag, updateDragging]
  )

  return (
    <div
      className={`drag-handle ${className}`}
      onPointerDown={handlePointerDown}
      onPointerEnter={() => updateHovering(true)}
      onPointerLeave={() => updateHovering(false)}
      style={{
        position: 'relative',
        width: 'var(--resize-divider-hit-width)',
        minWidth: 'var(--resize-divider-hit-width)',
        flexShrink: 0,
        cursor: 'col-resize',
        backgroundColor: 'transparent',
        zIndex: 10,
        ...style
      }}
      role="separator"
      aria-orientation="vertical"
      data-active={active ? 'true' : 'false'}
    />
  )
}

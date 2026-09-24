import { useEffect, useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { MessageSquare, X } from 'lucide-react'

const OFFSET = 4
const VIEWPORT_PADDING = 8
const MAX_HEIGHT = 320
const CLOSE_DELAY_MS = 100

interface PopoverPlacement {
  left: number
  top: number
  maxHeight: number
}

export function FeedbackPill({
  label,
  removeLabel,
  onRemove,
  keepOpen = false,
  children
}: {
  label: string
  removeLabel: string
  onRemove?: () => void
  keepOpen?: boolean
  children: ReactNode
}): JSX.Element {
  const [open, setOpen] = useState(false)
  const [placement, setPlacement] = useState<PopoverPlacement | null>(null)
  const anchorRef = useRef<HTMLDivElement>(null)
  const triggerRef = useRef<HTMLButtonElement>(null)
  const panelRef = useRef<HTMLDivElement>(null)
  const closeTimer = useRef<number | undefined>(undefined)
  const pointerInside = useRef(false)
  const keepOpenRef = useRef(keepOpen)
  keepOpenRef.current = keepOpen

  const cancelClose = (): void => window.clearTimeout(closeTimer.current)
  const show = (): void => {
    cancelClose()
    setOpen(true)
  }
  const scheduleClose = (): void => {
    cancelClose()
    closeTimer.current = window.setTimeout(() => {
      if (!keepOpenRef.current) setOpen(false)
    }, CLOSE_DELAY_MS)
  }
  const markInside = (): void => {
    pointerInside.current = true
  }

  useEffect(() => () => window.clearTimeout(closeTimer.current), [])

  useLayoutEffect(() => {
    if (!open) {
      setPlacement(null)
      return
    }
    pointerInside.current = false
    const place = (): void => {
      const anchor = anchorRef.current
      const panel = panelRef.current
      if (!anchor || !panel) return
      setPlacement(placeFeedbackPopover(
        anchor.getBoundingClientRect(),
        panel.offsetWidth,
        Math.min(panel.scrollHeight, MAX_HEIGHT),
        window.innerWidth,
        window.innerHeight
      ))
    }
    place()
    const content = panelRef.current?.firstElementChild
    const observer = content && typeof ResizeObserver !== 'undefined' ? new ResizeObserver(place) : null
    if (content) observer?.observe(content)
    const onPointerDown = (): void => {
      if (!pointerInside.current) setOpen(false)
      pointerInside.current = false
    }
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key !== 'Escape') return
      if (panelRef.current?.contains(document.activeElement)) triggerRef.current?.focus()
      setOpen(false)
    }
    window.addEventListener('resize', place)
    window.addEventListener('scroll', place, true)
    document.addEventListener('pointerdown', onPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      observer?.disconnect()
      window.removeEventListener('resize', place)
      window.removeEventListener('scroll', place, true)
      document.removeEventListener('pointerdown', onPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [open])

  return (
    <>
      <div
        ref={anchorRef}
        className="dc-feedback-pill"
        onMouseEnter={show}
        onMouseLeave={scheduleClose}
        onPointerDownCapture={markInside}
      >
        <button
          ref={triggerRef}
          type="button"
          className="dc-feedback-pill__trigger"
          aria-haspopup="dialog"
          aria-expanded={open}
          onClick={() => {
            cancelClose()
            setOpen((value) => !value)
          }}
        >
          <MessageSquare size={14} aria-hidden />
          <span className="dc-feedback-pill__label">{label}</span>
        </button>
        {onRemove && (
          <button type="button" className="dc-feedback-pill__remove" aria-label={removeLabel} onClick={onRemove}>
            <span>
              <X size={14} aria-hidden />
            </span>
          </button>
        )}
      </div>
      {open && createPortal(
        <div
          ref={panelRef}
          role="dialog"
          aria-label={label}
          className="dc-feedback-popover"
          style={{
            left: placement?.left ?? 0,
            top: placement?.top ?? 0,
            maxHeight: placement ? Math.min(MAX_HEIGHT, placement.maxHeight) : undefined,
            visibility: placement ? 'visible' : 'hidden'
          }}
          onMouseEnter={cancelClose}
          onMouseLeave={scheduleClose}
          onPointerDownCapture={markInside}
        >
          {children}
        </div>,
        document.body
      )}
    </>
  )
}

function placeFeedbackPopover(
  anchor: Pick<DOMRect, 'left' | 'top' | 'bottom'>,
  width: number,
  height: number,
  viewportWidth: number,
  viewportHeight: number
): PopoverPlacement {
  const above = anchor.top - OFFSET - VIEWPORT_PADDING
  const below = viewportHeight - anchor.bottom - OFFSET - VIEWPORT_PADDING
  const onTop = height <= above || above >= below
  const maxHeight = Math.max(0, onTop ? above : below)
  const shown = Math.min(height, maxHeight)
  return {
    left: Math.max(VIEWPORT_PADDING, Math.min(anchor.left, viewportWidth - width - VIEWPORT_PADDING)),
    top: onTop ? anchor.top - OFFSET - shown : anchor.bottom + OFFSET,
    maxHeight
  }
}

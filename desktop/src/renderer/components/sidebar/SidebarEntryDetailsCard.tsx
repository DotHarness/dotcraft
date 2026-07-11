import {
  Children,
  cloneElement,
  isValidElement,
  useEffect,
  useId,
  useLayoutEffect,
  useRef,
  useState,
  type CSSProperties,
  type FocusEvent,
  type HTMLAttributes,
  type JSX,
  type ReactElement,
  type ReactNode
} from 'react'
import { createPortal } from 'react-dom'

const OPEN_DELAY_MS = 320
const CLOSE_DELAY_MS = 120
const VIEWPORT_PADDING = 8
const CARD_OVERLAP = 8

export type SidebarEntryDetailsSide = 'left' | 'right'
export type SidebarEntryDetailsOverlapEdge = 'left' | 'right' | null

interface SidebarEntryDetailsRect {
  left: number
  right: number
  top: number
  width: number
  height: number
}

export interface SidebarEntryDetailsPlacement {
  left: number
  top: number
  side: SidebarEntryDetailsSide
  overlapEdge: SidebarEntryDetailsOverlapEdge
}

interface SidebarEntryDetailsCardProps {
  label: string
  content: ReactNode
  width: number
  interactive?: boolean
  disabled?: boolean
  onOpen?: () => void
  children: ReactNode
  wrapperStyle?: CSSProperties
}

export function SidebarEntryDetailsCard({
  label,
  content,
  width,
  interactive = false,
  disabled = false,
  onOpen,
  children,
  wrapperStyle
}: SidebarEntryDetailsCardProps): JSX.Element {
  const cardId = useId()
  const anchorRef = useRef<HTMLDivElement>(null)
  const cardRef = useRef<HTMLDivElement>(null)
  const openTimerRef = useRef<number | null>(null)
  const closeTimerRef = useRef<number | null>(null)
  const suppressNextFocusOpenRef = useRef(false)
  const [visible, setVisible] = useState(false)
  const [position, setPosition] = useState<SidebarEntryDetailsPlacement>({
    left: 0,
    top: 0,
    side: 'right',
    overlapEdge: null
  })

  const clearOpenTimer = (): void => {
    if (openTimerRef.current == null) return
    window.clearTimeout(openTimerRef.current)
    openTimerRef.current = null
  }
  const clearCloseTimer = (): void => {
    if (closeTimerRef.current == null) return
    window.clearTimeout(closeTimerRef.current)
    closeTimerRef.current = null
  }
  const show = (): void => {
    clearOpenTimer()
    clearCloseTimer()
    setVisible(true)
  }
  const scheduleShow = (): void => {
    if (disabled || visible || openTimerRef.current != null) return
    clearCloseTimer()
    openTimerRef.current = window.setTimeout(() => {
      openTimerRef.current = null
      setVisible(true)
    }, OPEN_DELAY_MS)
  }
  const scheduleHide = (): void => {
    clearOpenTimer()
    clearCloseTimer()
    closeTimerRef.current = window.setTimeout(() => {
      closeTimerRef.current = null
      setVisible(false)
    }, CLOSE_DELAY_MS)
  }

  useEffect(() => {
    if (!disabled) return
    clearOpenTimer()
    clearCloseTimer()
    setVisible(false)
  }, [disabled])

  useEffect(() => {
    if (visible) onOpen?.()
  }, [onOpen, visible])

  useEffect(() => () => {
    clearOpenTimer()
    clearCloseTimer()
  }, [])

  useLayoutEffect(() => {
    if (!visible) return
    const anchor = anchorRef.current
    const card = cardRef.current
    if (!anchor || !card) return
    const anchorRect = anchor.getBoundingClientRect()
    setPosition(placeSidebarEntryDetailsCard(
      anchorRect,
      {
        // offset dimensions stay stable while the entry animation applies a transform.
        width: card.offsetWidth || width,
        height: card.offsetHeight
      },
      window.innerWidth,
      window.innerHeight
    ))
  }, [content, visible, width])

  function handleAnchorBlur(event: FocusEvent<HTMLDivElement>): void {
    const next = event.relatedTarget as Node | null
    if (next && cardRef.current?.contains(next)) return
    scheduleHide()
  }

  const child = Children.only(children)
  const describedChild = isValidElement(child)
    ? cloneElement(child as ReactElement<HTMLAttributes<HTMLElement>>, {
        'aria-describedby': visible ? cardId : undefined
      })
    : child

  return (
    <>
      <div
        ref={anchorRef}
        tabIndex={interactive ? undefined : 0}
        aria-label={interactive ? undefined : label}
        aria-describedby={!interactive && visible ? cardId : undefined}
        onMouseEnter={scheduleShow}
        onMouseLeave={scheduleHide}
        onFocusCapture={(event) => {
          if (!interactive && event.target !== event.currentTarget) {
            clearOpenTimer()
            clearCloseTimer()
            setVisible(false)
            return
          }
          if (suppressNextFocusOpenRef.current) {
            suppressNextFocusOpenRef.current = false
            return
          }
          show()
        }}
        onBlurCapture={handleAnchorBlur}
        style={{ display: 'block', minWidth: 0, ...wrapperStyle }}
      >
        {describedChild}
      </div>
      {visible && createPortal(
        <div
          id={cardId}
          ref={cardRef}
          role={interactive ? 'dialog' : 'tooltip'}
          aria-label={interactive ? label : undefined}
          className="sidebar-entry-details-card"
          data-interactive={interactive ? 'true' : 'false'}
          data-side={position.side}
          data-overlap-edge={position.overlapEdge ?? undefined}
          style={{ position: 'fixed', left: position.left, top: position.top, width }}
          onMouseEnter={interactive ? () => { clearCloseTimer() } : undefined}
          onMouseLeave={interactive ? scheduleHide : undefined}
          onFocusCapture={interactive ? () => { clearCloseTimer() } : undefined}
          onBlurCapture={interactive ? (event) => {
            const next = event.relatedTarget as Node | null
            if (next && (cardRef.current?.contains(next) || anchorRef.current?.contains(next))) return
            scheduleHide()
          } : undefined}
          onKeyDown={interactive ? (event) => {
            if (event.key === 'Escape') {
              setVisible(false)
              const focusTarget = anchorRef.current?.querySelector<HTMLElement>('[tabindex],button')
              if (focusTarget) {
                suppressNextFocusOpenRef.current = true
                focusTarget.focus()
              }
            }
          } : undefined}
        >
          {content}
        </div>,
        document.body
      )}
    </>
  )
}

export function placeSidebarEntryDetailsCard(
  anchorRect: SidebarEntryDetailsRect,
  cardRect: Pick<SidebarEntryDetailsRect, 'width' | 'height'>,
  viewportWidth: number,
  viewportHeight: number
): SidebarEntryDetailsPlacement {
  const rightLeft = anchorRect.right - CARD_OVERLAP
  const leftLeft = anchorRect.left + CARD_OVERLAP - cardRect.width
  const fitsRight = rightLeft + cardRect.width <= viewportWidth - VIEWPORT_PADDING
  const side: SidebarEntryDetailsSide = fitsRight ? 'right' : 'left'
  const desiredLeft = side === 'right' ? rightLeft : leftLeft
  const left = clamp(
    desiredLeft,
    VIEWPORT_PADDING,
    viewportWidth - cardRect.width - VIEWPORT_PADDING
  )
  const top = clamp(
    anchorRect.top,
    VIEWPORT_PADDING,
    viewportHeight - cardRect.height - VIEWPORT_PADDING
  )
  const attached = Math.abs(left - desiredLeft) < 0.5

  return {
    left,
    top,
    side,
    overlapEdge: attached ? (side === 'right' ? 'left' : 'right') : null
  }
}

function clamp(value: number, min: number, max: number): number {
  if (max < min) return min
  return Math.min(Math.max(value, min), max)
}

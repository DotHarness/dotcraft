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
import { useTransientOverlay } from '../../hooks/useTransientOverlay'

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
  const suppressNextFocusOpenRef = useRef(false)
  const {
    visible,
    anchorRef,
    overlayRef,
    open,
    scheduleOpen,
    scheduleClose,
    hide,
    cancelClose
  } = useTransientOverlay<HTMLDivElement, HTMLDivElement>({
    disabled,
    interactive,
    openDelayMs: OPEN_DELAY_MS,
    closeDelayMs: CLOSE_DELAY_MS
  })
  const [position, setPosition] = useState<SidebarEntryDetailsPlacement>({
    left: 0,
    top: 0,
    side: 'right',
    overlapEdge: null
  })

  useEffect(() => {
    if (visible) onOpen?.()
  }, [onOpen, visible])

  useLayoutEffect(() => {
    if (!visible) return
    const anchor = anchorRef.current
    const card = overlayRef.current
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
  }, [content, visible, width, anchorRef, overlayRef])

  function handleAnchorFocus(event: FocusEvent<HTMLDivElement>): void {
    // A non-interactive card anchors on the wrapper itself; focus moving to a
    // child control means the user is acting, not inspecting — so hide.
    if (!interactive && event.target !== event.currentTarget) {
      hide()
      return
    }
    if (suppressNextFocusOpenRef.current) {
      suppressNextFocusOpenRef.current = false
      return
    }
    open()
  }

  function handleAnchorBlur(event: FocusEvent<HTMLDivElement>): void {
    const next = event.relatedTarget as Node | null
    if (next && overlayRef.current?.contains(next)) return
    scheduleClose()
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
        onMouseEnter={scheduleOpen}
        onMouseLeave={scheduleClose}
        onFocusCapture={handleAnchorFocus}
        onBlurCapture={handleAnchorBlur}
        style={{ display: 'block', minWidth: 0, ...wrapperStyle }}
      >
        {describedChild}
      </div>
      {visible && createPortal(
        <div
          id={cardId}
          ref={overlayRef}
          role={interactive ? 'dialog' : 'tooltip'}
          aria-label={interactive ? label : undefined}
          className="sidebar-entry-details-card"
          data-interactive={interactive ? 'true' : 'false'}
          data-side={position.side}
          data-overlap-edge={position.overlapEdge ?? undefined}
          style={{ position: 'fixed', left: position.left, top: position.top, width }}
          onMouseEnter={interactive ? cancelClose : undefined}
          onMouseLeave={interactive ? scheduleClose : undefined}
          onFocusCapture={interactive ? cancelClose : undefined}
          onBlurCapture={interactive ? (event) => {
            const next = event.relatedTarget as Node | null
            if (next && (overlayRef.current?.contains(next) || anchorRef.current?.contains(next))) return
            scheduleClose()
          } : undefined}
          onKeyDown={interactive ? (event) => {
            if (event.key === 'Escape') {
              hide()
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

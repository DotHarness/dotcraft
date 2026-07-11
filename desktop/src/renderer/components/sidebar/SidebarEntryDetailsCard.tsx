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
const CARD_GAP = 8

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
  const [position, setPosition] = useState({ left: 0, top: 0 })

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
    const cardRect = card.getBoundingClientRect()
    const fitsRight = anchorRect.right + CARD_GAP + cardRect.width <= window.innerWidth - VIEWPORT_PADDING
    const left = fitsRight
      ? anchorRect.right + CARD_GAP
      : anchorRect.left - CARD_GAP - cardRect.width
    setPosition({
      left: clamp(left, VIEWPORT_PADDING, window.innerWidth - cardRect.width - VIEWPORT_PADDING),
      top: clamp(anchorRect.top - 2, VIEWPORT_PADDING, window.innerHeight - cardRect.height - VIEWPORT_PADDING)
    })
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

function clamp(value: number, min: number, max: number): number {
  if (max < min) return min
  return Math.min(Math.max(value, min), max)
}

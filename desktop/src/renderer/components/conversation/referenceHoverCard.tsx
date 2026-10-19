import { useEffect, useId, useLayoutEffect, useRef, useState, type FocusEvent, type KeyboardEvent, type ReactNode, type ReactPortal } from 'react'
import { createPortal } from 'react-dom'
import { useTransientOverlay } from '../../hooks/useTransientOverlay'
import { DETAILS_CARD_CLOSE_DELAY_MS, DETAILS_CARD_OPEN_DELAY_MS } from '../sidebar/SidebarEntryDetailsCard'
import { ComposerOverlapBand, useComposerOverlapBandHeight } from './useComposerOverlapBand'

const CARD_GAP = 6
const CARD_RADIUS = 10
const CARD_WIDTH = 360
const VIEWPORT_PADDING = 8

export type ReferenceCardSide = 'below' | 'above'

export function placeReferenceCard(
  anchor: { left: number; top: number; bottom: number },
  cardHeight: number,
  viewport: { width: number; height: number },
  preferred: ReferenceCardSide
): { left: number; top: number; side: ReferenceCardSide } {
  const fitsBelow = anchor.bottom + CARD_GAP + cardHeight <= viewport.height - VIEWPORT_PADDING
  const fitsAbove = anchor.top - CARD_GAP - cardHeight >= VIEWPORT_PADDING
  const side: ReferenceCardSide = preferred === 'above'
    ? (fitsAbove || !fitsBelow ? 'above' : 'below')
    : (fitsBelow || !fitsAbove ? 'below' : 'above')
  const left = Math.max(VIEWPORT_PADDING, Math.min(anchor.left, viewport.width - CARD_WIDTH - VIEWPORT_PADDING))
  return { left, top: side === 'below' ? anchor.bottom + CARD_GAP : anchor.top - CARD_GAP - cardHeight, side }
}

function isKeyboardFocus(element: Element): boolean {
  try {
    return element.matches(':focus-visible')
  } catch {
    return true
  }
}

const TABBABLE = 'a[href]:not([tabindex="-1"]), button:not([disabled]):not([tabindex="-1"]), input:not([disabled]), select, textarea, [tabindex]:not([tabindex="-1"])'

function tabStops(root: ParentNode): HTMLElement[] {
  return Array.from(root.querySelectorAll<HTMLElement>(TABBABLE))
}

function focusAfter(from: HTMLElement, card: HTMLElement): void {
  const stops = tabStops(document).filter((element) => !card.contains(element) && element.getClientRects().length > 0)
  stops[stops.indexOf(from) + 1]?.focus()
}

export interface ReferenceHoverCardOptions {
  label: string
  className: string
  layoutKey: unknown
  compact?: boolean
  preferredSide?: ReferenceCardSide
  onOpen?: () => void
  overComposer?: boolean
  disabled?: boolean
  children: ReactNode
}

export interface ReferenceHoverCardAnchorProps<A extends HTMLElement> {
  ref: React.RefObject<A | null>
  'aria-describedby': string | undefined
  onMouseEnter: () => void
  onMouseLeave: () => void
  onFocus: (event: FocusEvent<A>) => void
  onBlur: (event: FocusEvent<A>) => void
  onKeyDown: (event: KeyboardEvent<A>) => void
}

export function useReferenceHoverCard<A extends HTMLElement>({
  label,
  className,
  layoutKey,
  compact = false,
  preferredSide = 'below',
  onOpen,
  overComposer = false,
  disabled = false,
  children
}: ReferenceHoverCardOptions): { anchorProps: ReferenceHoverCardAnchorProps<A>; card: ReactPortal | null } {
  const cardId = useId()
  const { visible, anchorRef, overlayRef, open, hide, scheduleOpen, scheduleClose, cancelClose } =
    useTransientOverlay<A, HTMLDivElement>({
      disabled,
      interactive: true,
      openDelayMs: DETAILS_CARD_OPEN_DELAY_MS,
      closeDelayMs: DETAILS_CARD_CLOSE_DELAY_MS
    })
  const [position, setPosition] = useState<{ left: number; top: number; side: ReferenceCardSide }>({
    left: 0,
    top: 0,
    side: preferredSide
  })
  const [placed, setPlaced] = useState(false)
  const returningFocus = useRef(false)

  useLayoutEffect(() => {
    if (!visible) {
      setPlaced(false)
      return
    }
    const anchor = anchorRef.current
    const card = overlayRef.current
    if (!anchor || !card) return
    const next = placeReferenceCard(
      anchor.getBoundingClientRect(),
      card.offsetHeight,
      { width: window.innerWidth, height: window.innerHeight },
      preferredSide
    )
    setPosition((current) =>
      current.left === next.left && current.top === next.top && current.side === next.side ? current : next
    )
    setPlaced(true)
  }, [visible, layoutKey, anchorRef, overlayRef, preferredSide])

  const overlapBandHeight = useComposerOverlapBandHeight(overlayRef, placed && overComposer, anchorRef)

  useEffect(() => {
    if (visible) onOpen?.()
  }, [visible, onOpen])

  // Window capture runs before the overlay's document-level Escape listener, which unmounts the card.
  useEffect(() => {
    if (!visible) return
    const onKeyDown = (event: globalThis.KeyboardEvent): void => {
      const anchor = anchorRef.current
      const card = overlayRef.current
      if (!anchor || !card || !card.contains(document.activeElement)) return
      const stops = tabStops(card)
      const atFirst = document.activeElement === stops[0]
      const atLast = document.activeElement === stops[stops.length - 1]
      if (event.key === 'Escape' || (event.key === 'Tab' && event.shiftKey && atFirst)) {
        event.preventDefault()
        returningFocus.current = true
        hide()
        anchor.focus()
      } else if (event.key === 'Tab' && !event.shiftKey && atLast) {
        event.preventDefault()
        hide()
        focusAfter(anchor, card)
      }
    }
    window.addEventListener('keydown', onKeyDown, true)
    return () => window.removeEventListener('keydown', onKeyDown, true)
  }, [visible, anchorRef, overlayRef, hide])

  const anchorProps: ReferenceHoverCardAnchorProps<A> = {
    ref: anchorRef,
    'aria-describedby': visible ? cardId : undefined,
    onMouseEnter: scheduleOpen,
    onMouseLeave: scheduleClose,
    onFocus: (event) => {
      if (returningFocus.current) {
        returningFocus.current = false
        return
      }
      if (isKeyboardFocus(event.currentTarget)) open()
    },
    onBlur: (event) => {
      if (event.relatedTarget && overlayRef.current?.contains(event.relatedTarget as Node)) return
      scheduleClose()
    },
    onKeyDown: (event) => {
      if (event.key !== 'Tab' || event.shiftKey || !visible) return
      const first = overlayRef.current ? tabStops(overlayRef.current)[0] : undefined
      if (!first) return
      event.preventDefault()
      first.focus()
    }
  }

  const card = visible
    ? createPortal(
        <div
          id={cardId}
          ref={overlayRef}
          role="dialog"
          aria-label={label}
          className={className}
          data-side={position.side}
          data-placed={placed ? 'true' : undefined}
          data-compact={compact ? 'true' : undefined}
          style={{ left: position.left, top: position.top }}
          onMouseEnter={cancelClose}
          onMouseLeave={scheduleClose}
          onBlur={(event) => {
            const next = event.relatedTarget as Node | null
            if (next && (overlayRef.current?.contains(next) || anchorRef.current?.contains(next))) return
            scheduleClose()
          }}
        >
          {overComposer && <ComposerOverlapBand height={overlapBandHeight} radius={CARD_RADIUS} />}
          {children}
        </div>,
        document.body
      )
    : null

  return { anchorProps, card }
}

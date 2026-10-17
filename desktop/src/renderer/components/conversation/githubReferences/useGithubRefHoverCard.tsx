import { useId, useLayoutEffect, useState, type FocusEvent, type MouseEvent, type ReactPortal } from 'react'
import { createPortal } from 'react-dom'
import { useTransientOverlay } from '../../../hooks/useTransientOverlay'
import type { GithubRefLoad } from '../../../stores/githubRefStore'
import { DETAILS_CARD_CLOSE_DELAY_MS, DETAILS_CARD_OPEN_DELAY_MS } from '../../sidebar/SidebarEntryDetailsCard'
import { GithubRefCardBody } from './GithubRefCard'

const CARD_GAP = 6
const CARD_WIDTH = 360
const VIEWPORT_PADDING = 8

export type GithubRefCardSide = 'below' | 'above'

export function placeGithubRefCard(
  anchor: { left: number; top: number; bottom: number },
  cardHeight: number,
  viewport: { width: number; height: number },
  preferred: GithubRefCardSide
): { left: number; top: number; side: GithubRefCardSide } {
  const fitsBelow = anchor.bottom + CARD_GAP + cardHeight <= viewport.height - VIEWPORT_PADDING
  const fitsAbove = anchor.top - CARD_GAP - cardHeight >= VIEWPORT_PADDING
  const side: GithubRefCardSide = preferred === 'above'
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

export function useGithubRefHoverCard<A extends HTMLElement>({
  load,
  label,
  preferredSide = 'below',
  onOpenLink
}: {
  load: GithubRefLoad
  label: string
  preferredSide?: GithubRefCardSide
  onOpenLink: (url: string, event: MouseEvent<HTMLElement>) => void
}): {
  anchorProps: {
    ref: React.RefObject<A | null>
    'aria-describedby': string | undefined
    onMouseEnter: () => void
    onMouseLeave: () => void
    onFocus: (event: FocusEvent<A>) => void
    onBlur: (event: FocusEvent<A>) => void
  }
  card: ReactPortal | null
} {
  const cardId = useId()
  const { visible, anchorRef, overlayRef, open, scheduleOpen, scheduleClose, cancelClose } =
    useTransientOverlay<A, HTMLDivElement>({
      interactive: true,
      openDelayMs: DETAILS_CARD_OPEN_DELAY_MS,
      closeDelayMs: DETAILS_CARD_CLOSE_DELAY_MS
    })
  const [position, setPosition] = useState<{ left: number; top: number; side: GithubRefCardSide }>({
    left: 0,
    top: 0,
    side: preferredSide
  })

  useLayoutEffect(() => {
    if (!visible) return
    const anchor = anchorRef.current
    const card = overlayRef.current
    if (!anchor || !card) return
    setPosition(placeGithubRefCard(
      anchor.getBoundingClientRect(),
      card.offsetHeight,
      { width: window.innerWidth, height: window.innerHeight },
      preferredSide
    ))
  }, [visible, load, anchorRef, overlayRef, preferredSide])

  const anchorProps = {
    ref: anchorRef,
    'aria-describedby': visible ? cardId : undefined,
    onMouseEnter: scheduleOpen,
    onMouseLeave: scheduleClose,
    onFocus: (event: FocusEvent<A>) => {
      if (isKeyboardFocus(event.currentTarget)) open()
    },
    onBlur: (event: FocusEvent<A>) => {
      if (event.relatedTarget && overlayRef.current?.contains(event.relatedTarget as Node)) return
      scheduleClose()
    }
  }

  const card = visible
    ? createPortal(
        <div
          id={cardId}
          ref={overlayRef}
          role="dialog"
          aria-label={label}
          className="dc-gh-card dc-gh-card--floating"
          data-side={position.side}
          data-compact={load.status === 'error' ? 'true' : undefined}
          style={{ left: position.left, top: position.top }}
          onMouseEnter={cancelClose}
          onMouseLeave={scheduleClose}
        >
          <GithubRefCardBody load={load} onOpenLink={onOpenLink} />
        </div>,
        document.body
      )
    : null

  return { anchorProps, card }
}

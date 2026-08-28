import { useLayoutEffect, useState, type CSSProperties, type JSX, type ReactNode, type RefObject } from 'react'
import { SIDEBAR_ROW_MIN_HEIGHT } from '../sidebar/sidebarNavRowStyles'
import { useReportComposerOverlayLift } from './composerOverlayLift'

/**
 * Shared visual language for the composer mention popovers (`@` files and `/`
 * commands·skills·system). Both render the same row shape — a standalone
 * type-colored icon, a bold name, and dimmed secondary text — under uppercase
 * section headers, inside one glass overlay with inset rounded highlight rows.
 * Keeping the styles here (rather than per popover) is what makes the two
 * surfaces read as one control.
 *
 * Per DESIGN.md: the overlay is the shared opaque elevated surface, rows are
 * borderless at rest, and the highlighted row uses neutral background elevation
 * (`--bg-tertiary`) — semantic color lives only in the small leading icon.
 */

export const MENTION_POPOVER_GAP = 8

/** The list's own padding is inside its max-height, so only the border adds to it. */
const MENTION_POPOVER_CHROME = 2
const CEILING_MARGIN = 8
const MENTION_POPOVER_MIN_HEIGHT = 96

interface MentionPopoverSurfaceProps {
  /** Outer popover element; measured for the mascot lift and used by callers for keyboard scroll-into-view. */
  popupRef: RefObject<HTMLDivElement | null>
  /** Whether the popover is shown (drives the lift measurement). */
  open: boolean
  role: string
  ariaLabel?: string
  /** Preferred list height in px; shrunk to whatever fits above the composer. */
  maxHeight: number
  children: ReactNode
}

/**
 * The lowest edge the popover may reach. Not the viewport: the composer sits
 * inside panels that clip their overflow, and the app chrome above them would
 * cover anything that escaped, so the ceiling is whichever clipping ancestor
 * starts lowest.
 */
function clipAncestors(from: Element): Element[] {
  const found: Element[] = []
  for (let el = from.parentElement; el && el !== document.documentElement; el = el.parentElement) {
    const { overflow, overflowX, overflowY } = getComputedStyle(el)
    if (overflow === 'visible' && overflowX === 'visible' && overflowY === 'visible') continue
    found.push(el)
  }
  return found
}

/**
 * The popover opens upward, so its height is bounded by the room between the
 * composer card and that ceiling — often less than the preferred height in a
 * short or windowed app, where the list would otherwise be cut off at the top.
 */
function useAvailableListHeight(
  popupRef: RefObject<HTMLElement | null>,
  open: boolean,
  preferred: number
): number {
  const [height, setHeight] = useState(preferred)

  useLayoutEffect(() => {
    const popup = popupRef.current
    if (!open || !popup) return undefined
    const card = popup.closest('[data-composer-card]')
    if (!card) {
      setHeight(preferred)
      return undefined
    }

    const clips = clipAncestors(card)

    const measure = (): void => {
      const ceiling = clips.reduce((top, el) => Math.max(top, el.getBoundingClientRect().top), 0)
      const room =
        card.getBoundingClientRect().top
        - ceiling
        - CEILING_MARGIN
        - MENTION_POPOVER_GAP
        - MENTION_POPOVER_CHROME
      const next = Math.max(MENTION_POPOVER_MIN_HEIGHT, Math.min(preferred, Math.floor(room)))
      setHeight((current) => (current === next ? current : next))
    }
    measure()

    // Panels resize without resizing the card, which moves it without resizing it.
    const observer = new ResizeObserver(measure)
    observer.observe(card)
    for (const el of clips) observer.observe(el)
    window.addEventListener('resize', measure)
    return () => {
      observer.disconnect()
      window.removeEventListener('resize', measure)
    }
  }, [open, popupRef, preferred])

  return height
}

/**
 * Outer surface for the composer mention popovers (`@` files and `/` commands).
 * It is mounted inside the card's padding, so it reaches back out over that
 * padding by `--composer-overlay-inset` to span the card edge to edge.
 */
export function MentionPopoverSurface({
  popupRef,
  open,
  role,
  ariaLabel,
  maxHeight,
  children
}: MentionPopoverSurfaceProps): JSX.Element {
  const listHeight = useAvailableListHeight(popupRef, open, maxHeight)
  useReportComposerOverlayLift(popupRef, open, MENTION_POPOVER_GAP)
  return (
    <div
      ref={popupRef}
      role={role}
      aria-label={ariaLabel}
      style={{
        position: 'absolute',
        bottom: '100%',
        left: 'calc(-1 * var(--composer-overlay-inset, 0px))',
        width: 'calc(100% + 2 * var(--composer-overlay-inset, 0px))',
        marginBottom: `calc(var(--composer-overlay-inset, 0px) + ${MENTION_POPOVER_GAP}px)`,
        zIndex: 50,
        boxShadow: 'var(--glass-shadow-soft)',
        background: 'var(--glass-surface-strong)',
        border: '1px solid var(--glass-border)',
        borderRadius: '12px',
        overflow: 'hidden',
        backdropFilter: 'var(--glass-blur)',
        WebkitBackdropFilter: 'var(--glass-blur)'
      }}
    >
      {/* Inset padding so the rounded row highlight floats inside the surface. */}
      <div style={{ maxHeight: listHeight, overflowY: 'auto', padding: '6px' }}>{children}</div>
    </div>
  )
}

export function MentionSectionHeader({ label }: { label: string }): JSX.Element {
  return (
    <div
      style={{
        padding: '6px 8px 3px',
        fontSize: '11px',
        color: 'var(--text-dimmed)',
        fontWeight: 600,
        textTransform: 'uppercase',
        letterSpacing: '0.04em'
      }}
    >
      {label}
    </div>
  )
}

export function mentionRowStyle(active: boolean): CSSProperties {
  return {
    display: 'flex',
    width: '100%',
    minHeight: SIDEBAR_ROW_MIN_HEIGHT,
    alignItems: 'center',
    gap: '9px',
    padding: '3px 8px',
    border: 'none',
    background: active ? 'var(--bg-tertiary)' : 'transparent',
    borderRadius: 'var(--sidebar-row-radius)',
    color: 'var(--text-primary)',
    cursor: 'pointer',
    textAlign: 'left',
    font: 'inherit'
  }
}

/** Fixed-width leading icon slot so every name aligns regardless of glyph width. */
export const mentionRowIconStyle: CSSProperties = {
  width: '18px',
  height: '18px',
  flexShrink: 0,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center'
}

export const mentionRowNameStyle: CSSProperties = {
  fontWeight: 600,
  fontSize: '13px',
  color: 'var(--text-primary)',
  whiteSpace: 'nowrap',
  flexShrink: 0,
  maxWidth: '60%',
  overflow: 'hidden',
  textOverflow: 'ellipsis'
}

export const mentionRowDescStyle: CSSProperties = {
  fontSize: '12px',
  color: 'var(--text-dimmed)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
  minWidth: 0,
  flex: 1,
  textAlign: 'right'
}

/** Wraps a leading icon and tints it (lucide glyphs inherit via currentColor). */
export function MentionRowIcon({ tint, children }: { tint?: string; children: ReactNode }): JSX.Element {
  return <span style={{ ...mentionRowIconStyle, color: tint }}>{children}</span>
}

/** Loading / empty / hint message rows. */
export const mentionEmptyStyle: CSSProperties = {
  padding: '8px 10px',
  fontSize: '12px',
  color: 'var(--text-dimmed)'
}

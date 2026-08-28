import { useLayoutEffect, useRef, useState, type RefObject } from 'react'

/**
 * Measures how far an upward-opening composer popup overlaps the composer card —
 * the same-tone `[data-composer-card]` surface (`--composer-input-background`,
 * which reads as one tone with the overlay's `--glass-surface-strong`).
 *
 * Returns the overlap height in px: the distance from the popup's bottom edge up
 * to the card's top edge, clamped to the popup height (0 when closed or no card is
 * found). Callers draw a hairline band of this height on the popup's bottom + side
 * edges so the seam is drawn exactly where the two same-tone surfaces meet, and the
 * part above the card (over the darker message area, where shadow separates it)
 * stays open. See specs/architecture/DESIGN.md (overlay overlap rule).
 */
export function useComposerOverlapBandHeight(
  popupRef: RefObject<HTMLElement | null>,
  open: boolean,
  /**
   * Optional trigger/anchor element used to locate the composer card when the
   * popup is rendered through a portal (e.g. ModelPicker), so it is no longer a
   * DOM descendant of the composer and `closest` from the popup cannot find it.
   */
  anchorRef?: RefObject<HTMLElement | null>
): number {
  const [height, setHeight] = useState(0)

  useLayoutEffect(() => {
    const popup = popupRef.current
    if (!open || !popup) {
      setHeight(0)
      return
    }
    // Toolbar popups live inside the card; the workspace-footer dropdowns sit
    // in the footer (a sibling below the card), so fall back to finding the card via
    // the composer root. Portaled popups resolve the card from the anchor instead,
    // since the popup itself is mounted on document.body.
    const anchor = anchorRef?.current ?? null
    const card =
      popup.closest('[data-composer-card]') ??
      popup.closest('[data-composer-root]')?.querySelector('[data-composer-card]') ??
      anchor?.closest('[data-composer-card]') ??
      anchor?.closest('[data-composer-root]')?.querySelector('[data-composer-card]') ??
      null
    if (!card) {
      setHeight(0)
      return
    }
    const measure = (): void => {
      const popupRect = popup.getBoundingClientRect()
      const cardRect = card.getBoundingClientRect()
      const overlap = Math.max(0, Math.min(popupRect.height, Math.round(popupRect.bottom - cardRect.top)))
      setHeight((current) => (current === overlap ? current : overlap))
    }
    measure()
    // The composer card grows as the input wraps; track it (and the popup) so the
    // band keeps stopping exactly at the card's top edge.
    const observer = new ResizeObserver(measure)
    observer.observe(card)
    observer.observe(popup)
    return () => observer.disconnect()
  }, [open, popupRef, anchorRef])

  return height
}

/**
 * Hairline drawn on the part of an upward-opening composer popup that overlaps the
 * composer card — its bottom edge plus the lower segments of the sides, up to
 * {height}px (from {@link useComposerOverlapBandHeight}). The top edge, over the
 * darker message area, stays open. The popup surface itself should be frameless.
 */
export function ComposerOverlapBand({ height, radius = 12 }: { height: number; radius?: number }): JSX.Element {
  const ref = useRef<HTMLSpanElement>(null)
  const [full, setFull] = useState(false)

  useLayoutEffect(() => {
    // When the popup is shorter than the overlap, the band spans the whole popup and
    // sits entirely over the same-tone surface. Close it into a full frame (top edge +
    // all-rounded corners) so the side hairlines hug the popup's rounded top corners
    // instead of leaving them poking past square corners.
    const popup = ref.current?.offsetParent as HTMLElement | null
    setFull(popup != null && height >= popup.offsetHeight - 1)
  }, [height])

  return (
    <span
      ref={ref}
      aria-hidden
      style={{
        position: 'absolute',
        left: 0,
        right: 0,
        bottom: 0,
        height,
        borderTop: full ? '1px solid var(--glass-border)' : 'none',
        borderLeft: '1px solid var(--glass-border)',
        borderRight: '1px solid var(--glass-border)',
        borderBottom: '1px solid var(--glass-border)',
        borderRadius: full ? `${radius}px` : `0 0 ${radius}px ${radius}px`,
        pointerEvents: 'none'
      }}
    />
  )
}

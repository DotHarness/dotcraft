/**
 * Cross-mount position handoff for the composer mascot.
 *
 * When an approval (or other decision UI) replaces the input composer, the
 * whole ComposerShell remounts, so the mascot cannot animate across the swap
 * by itself. The outgoing instance records its screen position in its layout
 * cleanup (the DOM node is still attached at that point); the incoming
 * instance consumes the record within a short freshness window and starts
 * from the recorded offset, riding to its own rim (FLIP).
 *
 * A single module-level slot is enough: only one composer dock exists per
 * window, and stale records expire via the freshness check.
 */

interface MascotHandoffRecord {
  top: number
  time: number
}

let record: MascotHandoffRecord | null = null

/** Call from the outgoing mascot's layout-effect cleanup (node still attached). */
export function recordMascotHandoff(el: HTMLElement): void {
  record = { top: el.getBoundingClientRect().top, time: performance.now() }
}

/**
 * Returns the vertical offset (px) from this element's resting position to the
 * recorded one, or null when there is nothing fresh to hand off or the move is
 * too small to animate. Positive = the old position was lower (ride up).
 */
export function consumeMascotHandoff(el: HTMLElement, maxAgeMs = 400): number | null {
  if (!record) return null
  const { top, time } = record
  record = null
  if (performance.now() - time > maxAgeMs) return null
  const dy = top - el.getBoundingClientRect().top
  return Math.abs(dy) < 8 ? null : dy
}

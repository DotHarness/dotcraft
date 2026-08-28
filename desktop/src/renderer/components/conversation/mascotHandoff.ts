/**
 * Cross-mount position handoff for the composer mascot: a decision UI replacing the
 * input composer remounts the whole ComposerShell, so the outgoing instance records
 * its screen position and the incoming one rides from that offset (FLIP).
 *
 * A single module-level slot is enough — only one composer dock exists per window,
 * and stale records expire via the freshness check.
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
 * Vertical offset (px) from this element's resting position to the recorded one, or
 * null when nothing fresh is pending. Positive = the old position was lower (ride up).
 */
export function consumeMascotHandoff(el: HTMLElement, maxAgeMs = 400): number | null {
  if (!record) return null
  const { top, time } = record
  record = null
  if (performance.now() - time > maxAgeMs) return null
  const dy = top - el.getBoundingClientRect().top
  return Math.abs(dy) < 8 ? null : dy
}

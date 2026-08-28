/**
 * The reveal advances a character cursor at a steady characters-per-second rate
 * decoupled from the bursty arrival of network deltas, speeding up proportionally
 * when it falls behind. These helpers hold no DOM or timing state.
 */

export interface RevealParams {
  /** Steady reveal rate while the cursor keeps pace with arriving text. */
  baseCps: number
  /** Backlog (in code points) tolerated before the cursor speeds up. */
  catchupThreshold: number
  /** Larger values make catch-up acceleration gentler. */
  catchupDivisor: number
  /** Hard cap on how much faster than `baseCps` catch-up may run. */
  maxCatchupMultiplier: number
}

export const DEFAULT_REVEAL_PARAMS: RevealParams = {
  baseCps: 80,
  catchupThreshold: 60,
  catchupDivisor: 120,
  maxCatchupMultiplier: 4
}

/** Minimum gap between committed reveal updates (~30fps) to bound re-renders. */
export const REVEAL_COMMIT_INTERVAL_MS = 33

/**
 * Effective characters-per-second given how far the reveal cursor lags the
 * received text. Within the threshold the rate is steady; beyond it the rate
 * scales up linearly, capped at `baseCps * maxCatchupMultiplier`.
 */
export function effectiveCps(backlog: number, params: RevealParams = DEFAULT_REVEAL_PARAMS): number {
  if (backlog <= params.catchupThreshold) return params.baseCps
  const scaled = params.baseCps * (1 + (backlog - params.catchupThreshold) / params.catchupDivisor)
  return Math.min(params.baseCps * params.maxCatchupMultiplier, scaled)
}

/**
 * Advance the (fractional) revealed count by `dtSeconds` toward `total`.
 * Never overshoots `total` and treats negative dt as zero.
 */
export function advanceReveal(
  revealed: number,
  total: number,
  dtSeconds: number,
  params: RevealParams = DEFAULT_REVEAL_PARAMS
): number {
  if (revealed >= total) return total
  const backlog = total - revealed
  const next = revealed + effectiveCps(backlog, params) * Math.max(0, dtSeconds)
  return next >= total ? total : next
}

/** Number of Unicode code points in `text` (so surrogate pairs count as one). */
export function codePointLength(text: string): number {
  let count = 0
  for (let offset = 0; offset < text.length; count++) {
    const first = text.charCodeAt(offset++)
    if (first >= 0xD800 && first <= 0xDBFF && offset < text.length) {
      const second = text.charCodeAt(offset)
      if (second >= 0xDC00 && second <= 0xDFFF) offset++
    }
  }
  return count
}

/**
 * Code-point-aware prefix of `text` (avoids splitting surrogate pairs such as
 * emoji or astral CJK). `count` is clamped to the valid range.
 */
export function sliceByCodePoints(text: string, count: number): string {
  if (count <= 0) return ''
  let offset = 0
  let seen = 0
  while (offset < text.length && seen < count) {
    const first = text.charCodeAt(offset++)
    if (first >= 0xD800 && first <= 0xDBFF && offset < text.length) {
      const second = text.charCodeAt(offset)
      if (second >= 0xDC00 && second <= 0xDFFF) offset++
    }
    seen++
  }
  return offset >= text.length ? text : text.slice(0, offset)
}

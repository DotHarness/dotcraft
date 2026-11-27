export const TOOLTIP_OPEN_DELAY_MS = 300

/** Entering a control this soon after another tooltip closes skips the open delay. */
const WARM_WINDOW_MS = 400

let shownCount = 0
let lastHiddenAt = Number.NEGATIVE_INFINITY

export function markTooltipShown(): () => void {
  shownCount += 1
  return () => {
    shownCount = Math.max(0, shownCount - 1)
    lastHiddenAt = Date.now()
  }
}

export function tooltipsWarm(): boolean {
  return shownCount > 0 || Date.now() - lastHiddenAt < WARM_WINDOW_MS
}

export function resetTooltipWarmupForTests(): void {
  shownCount = 0
  lastHiddenAt = Number.NEGATIVE_INFINITY
}

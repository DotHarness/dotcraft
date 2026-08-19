const COMPACT_COUNT_UNITS = ['', 'k', 'M', 'B'] as const

/** Formats a non-negative count with decimal compact units and at most one decimal place. */
export function formatCompactCount(value: number): string {
  const count = Math.round(value)
  if (count < 1_000) return String(count)

  let unitIndex = Math.min(
    Math.floor(Math.log10(count) / 3),
    COMPACT_COUNT_UNITS.length - 1
  )
  let rounded = roundToOneDecimal(count / 1_000 ** unitIndex)

  // Promote values that round to the next unit instead of displaying 1000k/1000M.
  if (rounded >= 1_000 && unitIndex < COMPACT_COUNT_UNITS.length - 1) {
    unitIndex++
    rounded = roundToOneDecimal(count / 1_000 ** unitIndex)
  }

  return `${rounded.toFixed(1).replace(/\.0$/, '')}${COMPACT_COUNT_UNITS[unitIndex]}`
}

function roundToOneDecimal(value: number): number {
  return Math.round(value * 10) / 10
}

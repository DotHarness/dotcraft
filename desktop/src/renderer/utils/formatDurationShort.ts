export function formatDurationShort(totalMs: number): string {
  const safeMs = Number.isFinite(totalMs) ? Math.max(0, totalMs) : 0
  const totalSeconds = Math.max(1, Math.round(safeMs / 1000))

  if (totalSeconds < 60) {
    return `${totalSeconds}s`
  }

  const totalMinutes = Math.floor(totalSeconds / 60)
  const secondsRemainder = totalSeconds % 60
  if (totalMinutes < 60) {
    return `${totalMinutes}m ${String(secondsRemainder).padStart(2, '0')}s`
  }

  const hours = Math.floor(totalMinutes / 60)
  const minutesRemainder = totalMinutes % 60
  return `${hours}h ${String(minutesRemainder).padStart(2, '0')}m`
}

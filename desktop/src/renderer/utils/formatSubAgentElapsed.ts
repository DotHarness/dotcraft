export function formatSubAgentElapsed(totalMs: number): string {
  const safeMs = Number.isFinite(totalMs) ? Math.max(0, totalMs) : 0
  let remainingSeconds = Math.floor(safeMs / 1000)

  const days = Math.floor(remainingSeconds / 86_400)
  remainingSeconds %= 86_400
  const hours = Math.floor(remainingSeconds / 3_600)
  remainingSeconds %= 3_600
  const minutes = Math.floor(remainingSeconds / 60)
  const seconds = remainingSeconds % 60

  if (days > 0) return `${days}d ${hours}h ${minutes}m ${seconds}s`
  if (hours > 0) return `${hours}h ${minutes}m ${seconds}s`
  if (minutes > 0) return `${minutes}m ${seconds}s`
  return `${seconds}s`
}

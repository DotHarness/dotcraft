export function estimateQueuedInputDockHeightPx(queuedInputCount: number): number {
  if (queuedInputCount === 0) return 0
  const headerHeight = 28
  const rowsHeight = queuedInputCount * 26 + (queuedInputCount - 1) * 3
  const bottomPadding = 8
  return headerHeight + rowsHeight + bottomPadding
}

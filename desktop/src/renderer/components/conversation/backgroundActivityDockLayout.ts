interface BackgroundActivityDockHeightInput {
  queuedInputCount: number
  subAgentChildCount: number
  subAgentCollapsed: boolean
}

export function estimateBackgroundActivityDockHeightPx({
  queuedInputCount,
  subAgentChildCount,
  subAgentCollapsed
}: BackgroundActivityDockHeightInput): number {
  const hasQueue = queuedInputCount > 0
  const hasSubAgents = subAgentChildCount > 0
  if (!hasQueue && !hasSubAgents) return 0

  const headerHeight = 28
  const queueRowsHeight = hasQueue
    ? queuedInputCount * 26 + Math.max(0, queuedInputCount - 1) * 3
    : 0
  const queueSectionLabelHeight = hasQueue && hasSubAgents ? 20 : 0
  const queueSectionBottomPadding = hasQueue ? (hasSubAgents ? 7 : 8) : 0
  const subAgentRowsHeight = hasSubAgents && !subAgentCollapsed
    ? Math.min(subAgentChildCount * 28 + 8, 180)
    : 0

  return headerHeight + queueSectionLabelHeight + queueRowsHeight + queueSectionBottomPadding + subAgentRowsHeight
}

import type { ChatGptUsageSnapshot, ChatGptUsageWindow } from '../stores/providersStore'

export type ChatGptUsageWindowKind = 'fiveHour' | 'weekly' | 'primary' | 'secondary'

export interface ChatGptUsageDisplayWindow {
  kind: ChatGptUsageWindowKind
  window: ChatGptUsageWindow
}

const DURATION_TOLERANCE = 0.05
const FIVE_HOURS_SECONDS = 5 * 60 * 60
const ONE_WEEK_SECONDS = 7 * 24 * 60 * 60

export function shapeChatGptUsageWindows(usage: ChatGptUsageSnapshot | null): ChatGptUsageDisplayWindow[] {
  if (!usage?.available) return []

  const windows: ChatGptUsageDisplayWindow[] = []
  addWindow(windows, usage.primary, 'primary')
  addWindow(windows, usage.secondary, 'secondary')
  windows.sort((left, right) => rank(left.kind) - rank(right.kind))
  return windows
}

export function classifyChatGptUsageWindow(
  window: ChatGptUsageWindow,
  fallback: 'primary' | 'secondary'
): ChatGptUsageWindowKind {
  if (isApproximate(window.windowSeconds, FIVE_HOURS_SECONDS)) return 'fiveHour'
  if (isApproximate(window.windowSeconds, ONE_WEEK_SECONDS)) return 'weekly'
  return fallback
}

function addWindow(
  windows: ChatGptUsageDisplayWindow[],
  window: ChatGptUsageWindow | null,
  fallback: 'primary' | 'secondary'
): void {
  if (window) windows.push({ window, kind: classifyChatGptUsageWindow(window, fallback) })
}

function isApproximate(actual: number, expected: number): boolean {
  const ratio = actual / expected
  return ratio >= 1 - DURATION_TOLERANCE && ratio <= 1 + DURATION_TOLERANCE
}

function rank(kind: ChatGptUsageWindowKind): number {
  switch (kind) {
    case 'fiveHour': return 0
    case 'weekly': return 1
    case 'primary': return 2
    case 'secondary': return 3
  }
}

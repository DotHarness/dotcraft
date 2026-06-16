export const AGENT_BUILDER_CHAT_DEFAULT_WIDTH = 520
export const AGENT_BUILDER_CHAT_MIN_WIDTH = 360
export const AGENT_BUILDER_MAIN_MIN_WIDTH = 640
export const AGENT_BUILDER_CHAT_DEFAULT_WIDTH_RATIO = 0.38

export function resolveMaxAgentBuilderChatWidth(splitWidth: number | null | undefined): number {
  if (splitWidth == null || splitWidth <= 0) return Number.POSITIVE_INFINITY
  return Math.max(AGENT_BUILDER_CHAT_MIN_WIDTH, splitWidth - AGENT_BUILDER_MAIN_MIN_WIDTH)
}

export function resolveAgentBuilderChatWidth(
  fallbackWidth: number,
  widthRatio: number,
  splitWidth: number | null | undefined
): number {
  const safeFallback = Number.isFinite(fallbackWidth)
    ? Math.max(AGENT_BUILDER_CHAT_MIN_WIDTH, fallbackWidth)
    : AGENT_BUILDER_CHAT_DEFAULT_WIDTH

  if (splitWidth == null || splitWidth <= 0) return safeFallback

  const safeRatio = Number.isFinite(widthRatio) && widthRatio > 0
    ? widthRatio
    : AGENT_BUILDER_CHAT_DEFAULT_WIDTH_RATIO
  const byRatio = Math.max(AGENT_BUILDER_CHAT_MIN_WIDTH, Math.round(splitWidth * safeRatio))
  return Math.min(byRatio, resolveMaxAgentBuilderChatWidth(splitWidth))
}

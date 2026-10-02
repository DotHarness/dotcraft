import type { DesktopPluginConversationAsideLayout } from '@dotcraft/plugin'

export const SHIFT_MIN_SIDE_PX = 180
export const GUTTER_MIN_SIDE_PX = 400
export const PINNED_SHIFT_PX = 153
export const RAIL_LANE_PX = 52

export interface ConversationAsideLayout {
  tier: DesktopPluginConversationAsideLayout
  side: number
  shift: number
  leadingStart: number
  leadingWidth: number
  trailingWidth: number
}

export function conversationAsideTier(side: number): DesktopPluginConversationAsideLayout {
  if (side >= GUTTER_MIN_SIDE_PX) return 'gutter'
  if (side >= SHIFT_MIN_SIDE_PX) return 'shift'
  return 'overlay'
}

export function conversationAsideLayout(
  streamWidth: number,
  readingWidth: number,
  pinned: boolean,
  railShown: boolean
): ConversationAsideLayout {
  const side = Math.max(0, (streamWidth - readingWidth) / 2)
  const tier = conversationAsideTier(side)
  const shift = pinned && tier === 'shift' ? PINNED_SHIFT_PX : 0
  const leadingStart = railShown ? RAIL_LANE_PX : 0
  return {
    tier,
    side,
    shift,
    leadingStart,
    leadingWidth: Math.max(0, side - shift - leadingStart),
    trailingWidth: side + shift
  }
}

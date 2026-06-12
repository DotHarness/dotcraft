import type { AppLocale } from './locales'

export type AddTabMenuAction =
  | 'openFile'
  | 'newBrowser'
  | 'newTerminal'
  | 'newChanges'
  | 'newPlan'

export interface AddTabMenuItem {
  action: AddTabMenuAction
  label: string
  shortcut?: string
  enabled: boolean
}

export interface AddTabMenuAnchor {
  left: number
  top: number
  right: number
  bottom: number
}

export interface AddTabMenuPosition {
  left: number
  top: number
  width: number
}

export interface AddTabMenuRequest {
  x: number
  y: number
  anchor?: AddTabMenuAnchor
  theme: 'dark' | 'light'
  locale?: AppLocale
  items: AddTabMenuItem[]
}

export interface AddTabPopupPayload extends AddTabMenuRequest {
  position: AddTabMenuPosition
}

const PopupWidth = 210
const PopupItemHeight = 32
const PopupVerticalPadding = 8
const ViewportMargin = 8
const AnchorGap = 4

function clamp(value: number, min: number, max: number): number {
  if (max < min) return min
  return Math.min(Math.max(value, min), max)
}

export function resolveAddTabPopupPayload(
  payload: AddTabMenuRequest,
  viewport: { width: number; height: number }
): AddTabPopupPayload {
  const anchor = payload.anchor ?? {
    left: payload.x,
    top: payload.y,
    right: payload.x,
    bottom: payload.y
  }
  const height = payload.items.length * PopupItemHeight + PopupVerticalPadding
  const left = clamp(anchor.left, ViewportMargin, viewport.width - PopupWidth - ViewportMargin)
  const belowTop = payload.y
  const aboveTop = anchor.top - height - AnchorGap
  const top =
    belowTop + height > viewport.height - ViewportMargin && aboveTop >= ViewportMargin
      ? aboveTop
      : clamp(belowTop, ViewportMargin, viewport.height - height - ViewportMargin)

  return {
    ...payload,
    position: {
      left,
      top,
      width: PopupWidth
    }
  }
}

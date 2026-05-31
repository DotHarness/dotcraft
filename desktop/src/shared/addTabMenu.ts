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

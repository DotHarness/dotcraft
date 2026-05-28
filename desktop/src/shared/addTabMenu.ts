export type AddTabMenuAction = 'openFile' | 'newBrowser' | 'newTerminal'

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
  items: AddTabMenuItem[]
}

export interface AddTabPopupPayload extends AddTabMenuRequest {
  position: AddTabMenuPosition
}

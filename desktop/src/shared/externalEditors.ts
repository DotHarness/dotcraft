export type EditorId =
  | 'explorer'
  | 'vs'
  | 'cursor'
  | 'vscode'
  | 'rider'
  | 'webstorm'
  | 'idea'
  | 'github-desktop'
  | 'git-bash'
  | 'terminal'

export interface EditorInfo {
  id: EditorId
  labelKey: string
  iconKey: string
  iconDataUrl?: string
}

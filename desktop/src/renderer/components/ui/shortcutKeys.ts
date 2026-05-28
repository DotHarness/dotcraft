export type ShortcutToken = 'Mod' | 'Ctrl' | 'Shift' | 'Alt' | 'Enter' | 'Esc' | string
export type ShortcutSpec = readonly ShortcutToken[]

export const ACTION_SHORTCUTS = {
  newThread: ['Mod', 'N'],
  search: ['Mod', 'K'],
  toggleSidebar: ['Mod', 'B'],
  toggleDetailPanel: ['Mod', 'Shift', 'B'],
  quickOpen: ['Mod', 'P'],
  settings: ['Mod', ','],
  send: ['Enter'],
  cancel: ['Esc'],
  toggleMode: ['Shift', 'Tab'],
  selectModel: ['Mod', 'Shift', 'M']
} as const satisfies Record<string, ShortcutSpec>

export function isMacPlatform(platform = getPlatform()): boolean {
  return /^(Mac|iPhone|iPad|iPod)/i.test(platform)
}

export function formatShortcutParts(
  shortcut: ShortcutSpec | undefined,
  platform = getPlatform()
): string[] {
  if (!shortcut) return []
  const mod = isMacPlatform(platform) ? 'Cmd' : 'Ctrl'
  return shortcut.map((part) => {
    if (part === 'Mod') return mod
    if (part === 'Esc') return 'Esc'
    return part
  })
}

function getPlatform(): string {
  if (typeof navigator === 'undefined') return ''
  return navigator.platform
}

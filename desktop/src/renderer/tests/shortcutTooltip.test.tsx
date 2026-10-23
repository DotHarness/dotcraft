import { describe, expect, it } from 'vitest'
import { ACTION_SHORTCUTS, formatShortcutParts } from '../components/ui/shortcutKeys'

describe('shortcut formatting', () => {
  it('formats Mod as Ctrl on Windows and Linux and as Cmd on macOS', () => {
    expect(formatShortcutParts(ACTION_SHORTCUTS.toggleDetailPanel, 'Win32')).toEqual(['Ctrl', 'Shift', 'B'])
    expect(formatShortcutParts(ACTION_SHORTCUTS.settings, 'Linux x86_64')).toEqual(['Ctrl', ','])
    expect(formatShortcutParts(ACTION_SHORTCUTS.quickOpen, 'MacIntel')).toEqual(['Cmd', 'P'])
  })
})

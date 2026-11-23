import { createLucideIcon } from 'lucide-react'

export const TERMINAL_GLYPH_FRAME = { x: 3, y: 3, size: 18, radius: 4.5 }
export const TERMINAL_GLYPH_PROMPT = 'm7.5 9.5 2.5 2.5-2.5 2.5'
export const TERMINAL_GLYPH_CURSOR = 'M12.5 14.5h4'

export const TerminalGlyph = createLucideIcon('terminal-glyph', [
  ['rect', {
    x: String(TERMINAL_GLYPH_FRAME.x),
    y: String(TERMINAL_GLYPH_FRAME.y),
    width: String(TERMINAL_GLYPH_FRAME.size),
    height: String(TERMINAL_GLYPH_FRAME.size),
    rx: String(TERMINAL_GLYPH_FRAME.radius),
    key: 'frame'
  }],
  ['path', { d: TERMINAL_GLYPH_PROMPT, key: 'prompt' }],
  ['path', { d: TERMINAL_GLYPH_CURSOR, key: 'cursor' }]
])

import { create } from 'zustand'
import type { ConversationItem } from '../types/conversation'

/** Interactive Tool UI display modes (MCP Apps): inline (default), floating pip, or fullscreen overlay. */
export type DisplayMode = 'inline' | 'pip' | 'fullscreen'

export const AVAILABLE_DISPLAY_MODES: DisplayMode[] = ['inline', 'pip', 'fullscreen']

/** Below this window width, `pip` is coerced to `fullscreen` (a floating window is impractical). */
const NARROW_WINDOW_WIDTH = 640

interface ExpandedCard {
  item: ConversationItem
  threadId: string | null
  mode: 'pip' | 'fullscreen'
}

interface DisplayModeState {
  /** The single card currently expanded (pip/fullscreen), or null when all cards are inline. */
  expanded: ExpandedCard | null
  /**
   * Arbitrate a UI `requestDisplayMode`. Returns the **granted** mode (may differ from requested:
   * `pip` coerces to `fullscreen` on a narrow window). Expanding one card collapses any other.
   */
  requestMode: (item: ConversationItem, threadId: string | null, mode: DisplayMode) => DisplayMode
  collapse: () => void
}

export const useDisplayModeStore = create<DisplayModeState>((set) => ({
  expanded: null,
  requestMode: (item, threadId, mode) => {
    if (mode === 'inline') {
      set((s) => (s.expanded?.item.id === item.id ? { expanded: null } : s))
      return 'inline'
    }
    const narrow = typeof window !== 'undefined' && window.innerWidth < NARROW_WINDOW_WIDTH
    const granted: 'pip' | 'fullscreen' = mode === 'pip' && narrow ? 'fullscreen' : mode
    set({ expanded: { item, threadId, mode: granted } })
    return granted
  },
  collapse: () => set({ expanded: null })
}))

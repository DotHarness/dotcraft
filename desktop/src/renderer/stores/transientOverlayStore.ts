import { create } from 'zustand'

/**
 * Tracks the stack of open "layers" — modals, menus, popovers, and any floating
 * surface that renders above ordinary content. Transient hover overlays
 * (tooltips, detail cards) read `topDepth` to know whether something opened
 * *above* them and should therefore suppress/close them, even when no
 * `mouseleave` fired (e.g. a modal appeared under a stationary pointer).
 *
 * Depth is the layer's own nesting level (base content = 0, a modal in base
 * content = 1, a modal opened from within that modal = 2, ...), assigned by
 * `useLayerPresence` from `LayerContext`. A hover overlay at depth `d` is
 * suppressed while `topDepth > d`.
 */
interface TransientOverlayState {
  /** Depths of every currently-open layer (multiset; duplicates allowed). */
  openDepths: number[]
  /** Highest open layer depth, or 0 when nothing is open. */
  topDepth: number
  /** Number of fullscreen layers that must cover Electron native views. */
  nativeViewBlockerCount: number
  /** Register an open layer at `depth`. */
  pushLayer(depth: number): void
  /** Remove one previously registered layer at `depth`. */
  popLayer(depth: number): void
  /** Register a fullscreen layer that must temporarily hide native views. */
  pushNativeViewBlocker(): void
  /** Remove one previously registered native-view blocker. */
  popNativeViewBlocker(): void
}

function maxDepth(depths: number[]): number {
  return depths.length > 0 ? Math.max(...depths) : 0
}

export const useTransientOverlayStore = create<TransientOverlayState>((set) => ({
  openDepths: [],
  topDepth: 0,
  nativeViewBlockerCount: 0,
  pushLayer(depth) {
    set((state) => {
      const openDepths = [...state.openDepths, depth]
      return { openDepths, topDepth: maxDepth(openDepths) }
    })
  },
  popLayer(depth) {
    set((state) => {
      const index = state.openDepths.indexOf(depth)
      if (index === -1) return state
      const openDepths = state.openDepths.slice()
      openDepths.splice(index, 1)
      return { openDepths, topDepth: maxDepth(openDepths) }
    })
  },
  pushNativeViewBlocker() {
    set((state) => ({ nativeViewBlockerCount: state.nativeViewBlockerCount + 1 }))
  },
  popNativeViewBlocker() {
    set((state) => ({ nativeViewBlockerCount: Math.max(0, state.nativeViewBlockerCount - 1) }))
  }
}))

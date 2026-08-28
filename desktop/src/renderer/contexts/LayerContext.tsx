import { createContext, useContext, useEffect, type JSX, type ReactNode } from 'react'
import { useTransientOverlayStore } from '../stores/transientOverlayStore'

/**
 * Nesting depth of the surface a component renders in: base content is `0`, a modal
 * opened from it is `1`, and so on. Transient hover overlays read this to decide
 * whether a layer opened *above* them (see `transientOverlayStore`).
 */
export const LayerContext = createContext(0)

/**
 * Registers the caller as an open layer for its lifetime, so hover overlays beneath
 * it suppress while it is open. Pass `active={false}` for a component that stays
 * mounted but is only sometimes a layer.
 */
export function useLayerPresence(active = true, blocksNativeViews = false): number {
  const parentDepth = useContext(LayerContext)
  const depth = parentDepth + 1

  useEffect(() => {
    if (!active) return
    const { pushLayer, popLayer } = useTransientOverlayStore.getState()
    pushLayer(depth)
    return () => popLayer(depth)
  }, [active, depth])

  useEffect(() => {
    if (!active || !blocksNativeViews) return
    const { pushNativeViewBlocker, popNativeViewBlocker } = useTransientOverlayStore.getState()
    pushNativeViewBlocker()
    return () => popNativeViewBlocker()
  }, [active, blocksNativeViews])

  return depth
}

/**
 * Wrap a layer's content so it registers as an open layer and its children see the
 * deeper depth, which keeps tooltips inside the layer working.
 */
export function LayerBoundary({
  active = true,
  blocksNativeViews = false,
  children
}: {
  active?: boolean
  /** Hide Electron native views while this fullscreen layer is mounted. */
  blocksNativeViews?: boolean
  children: ReactNode
}): JSX.Element {
  const depth = useLayerPresence(active, blocksNativeViews)
  return <LayerContext.Provider value={depth}>{children}</LayerContext.Provider>
}

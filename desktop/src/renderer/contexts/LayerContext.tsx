import { createContext, useContext, useEffect, type JSX, type ReactNode } from 'react'
import { useTransientOverlayStore } from '../stores/transientOverlayStore'

/**
 * The nesting depth of the surface a component renders in: base content is `0`,
 * a modal/menu/popover opened from base content is `1`, one opened from within
 * that layer is `2`, and so on. Transient hover overlays read this to decide
 * whether a layer opened *above* them (see `transientOverlayStore`).
 */
export const LayerContext = createContext(0)

/**
 * Registers the calling component as an open layer for its lifetime: it pushes
 * its depth (`parent + 1`) onto the transient-overlay store on mount and pops it
 * on unmount, so hover tooltips/cards beneath it suppress while it is open.
 * Returns the assigned depth to provide to children via `LayerContext`.
 *
 * Pass `active={false}` for a component that stays mounted but is only sometimes
 * a layer; conditionally-rendered dialogs can leave it defaulted to `true`.
 */
export function useLayerPresence(active = true): number {
  const parentDepth = useContext(LayerContext)
  const depth = parentDepth + 1

  useEffect(() => {
    if (!active) return
    const { pushLayer, popLayer } = useTransientOverlayStore.getState()
    pushLayer(depth)
    return () => popLayer(depth)
  }, [active, depth])

  return depth
}

/**
 * Wraps a modal/menu/popover so that (a) it registers as an open layer and
 * (b) its own children see the deeper `LayerContext` depth — which keeps
 * tooltips rendered *inside* the layer working while suppressing those beneath
 * it. Wrap the layer's content (e.g. the portaled dialog body).
 */
export function LayerBoundary({
  active = true,
  children
}: {
  active?: boolean
  children: ReactNode
}): JSX.Element {
  const depth = useLayerPresence(active)
  return <LayerContext.Provider value={depth}>{children}</LayerContext.Provider>
}

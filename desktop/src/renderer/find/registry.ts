// Module-level rather than React context: the shortcut handler needs to know whether
// anything is searchable outside of render.
import type { FindSurface } from './types'

const surfaces = new Map<string, FindSurface>()
const listeners = new Set<() => void>()

export function registerFindSurface(surface: FindSurface): () => void {
  surfaces.set(surface.id, surface)
  notify()
  return () => {
    if (surfaces.get(surface.id) === surface) {
      surfaces.delete(surface.id)
      notify()
    }
  }
}

/** Highest priority first; ties keep registration order. */
export function listFindSurfaces(): FindSurface[] {
  return [...surfaces.values()].sort((left, right) => right.priority - left.priority)
}

export function getFindSurface(id: string): FindSurface | undefined {
  return surfaces.get(id)
}

export function hasFindSurfaces(): boolean {
  return surfaces.size > 0
}

export function subscribeToFindSurfaces(listener: () => void): () => void {
  listeners.add(listener)
  return () => { listeners.delete(listener) }
}

function notify(): void {
  for (const listener of listeners) listener()
}

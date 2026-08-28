import { useEffect, useRef } from 'react'
import { registerFindSurface } from './registry'
import { useFindStore } from '../stores/findStore'
import type { FindMatch, FindSegment, FindSurface } from './types'

export interface UseFindSurfaceOptions {
  /** Stable identity, e.g. `file:<path>`; `undefined` skips registration. */
  id: string | undefined
  domain: FindSurface['domain']
  priority: number
  getSegments: () => FindSegment[]
  getContainer: () => HTMLElement | null
  reveal?: (match: FindMatch) => void
  resolveElement?: (match: FindMatch) => HTMLElement | null
  /** A change means the surface's text changed. Keep it cheap: a length, a version. */
  contentKey?: string | number
}

// Callbacks live in a ref so rebuilding them does not re-register the surface.
// Only identity or the presence of an optional capability changes registration.
export function useFindSurface(options: UseFindSurfaceOptions): void {
  const { id, domain, priority, contentKey } = options
  const hasReveal = options.reveal !== undefined
  const hasResolveElement = options.resolveElement !== undefined
  const latest = useRef(options)
  latest.current = options

  useEffect(() => {
    if (id === undefined) return
    return registerFindSurface({
      id,
      domain,
      priority,
      getSegments: () => latest.current.getSegments(),
      getContainer: () => latest.current.getContainer(),
      ...(hasReveal
        ? { reveal: (match: FindMatch) => latest.current.reveal?.(match) }
        : {}),
      ...(hasResolveElement
        ? { resolveElement: (match: FindMatch) => latest.current.resolveElement?.(match) ?? null }
        : {})
    })
  }, [id, domain, priority, hasReveal, hasResolveElement])

  useEffect(() => {
    if (id === undefined) return
    useFindStore.getState().refresh()
  }, [id, contentKey])
}

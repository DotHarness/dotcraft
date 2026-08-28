import { useEffect } from 'react'
import { activeFindMatch, useFindStore } from '../stores/findStore'
import { applyHighlights, canDecorate, clearHighlights, rangesIn, revealRange } from './decorate'
import { getFindSurface, listFindSurfaces } from './registry'
import type { FindMatch } from './types'

function revealVirtualizedMatch(match: FindMatch): boolean {
  const surface = getFindSurface(match.surfaceId)
  if (surface?.reveal === undefined) return false
  surface.reveal(match)
  return true
}

function rowElement(match: FindMatch): HTMLElement | null {
  const surface = getFindSurface(match.surfaceId)
  const resolved = surface?.resolveElement?.(match)
  if (resolved != null) return resolved
  const container = surface?.getContainer() ?? null
  if (container === null || match.lineId === undefined) return null
  const line = `[data-line="${CSS.escape(match.lineId)}"]`
  const selector = match.scopeSelector === undefined ? line : `${match.scopeSelector} ${line}`
  return container.querySelector<HTMLElement>(selector)
}

function repaint(query: string, active: FindMatch | undefined): Range | undefined {
  const trimmed = query.trim()
  if (trimmed.length === 0) {
    clearHighlights()
    return undefined
  }

  const all: Range[] = []
  let activeRange: Range | undefined

  for (const surface of listFindSurfaces()) {
    const container = surface.getContainer()
    if (container === null) continue
    const ranges = rangesIn(container, trimmed)
    if (active !== undefined && active.surfaceId === surface.id && activeRange === undefined) {
      const row = rowElement(active)
      if (row !== null) {
        // Several matches can share a row, so the occurrence index is what
        // distinguishes them once the model offsets are gone.
        const inRow = ranges.filter((range) => row.contains(range.startContainer))
        activeRange = inRow[active.occurrence]
      }
    }
    all.push(...ranges)
  }

  applyHighlights(all, activeRange)
  return activeRange
}

export function useFindDecoration(): void {
  const open = useFindStore((state) => state.open)
  const query = useFindStore((state) => state.query)
  const revision = useFindStore((state) => state.revision)
  const active = useFindStore(activeFindMatch)

  useEffect(() => {
    if (!open || !canDecorate()) {
      clearHighlights()
      return
    }
    const revealedBySurface = active !== undefined && revealVirtualizedMatch(active)

    let frame = 0
    const schedule = (): void => {
      cancelAnimationFrame(frame)
      // At most one repaint per frame: a virtualized list mutates continuously while
      // scrolling, and re-walking its text on each mutation costs more than the scroll.
      frame = requestAnimationFrame(() => { repaint(query, active) })
    }

    // Immediate: only mutation-driven repaints are coalesced, and an occluded window
    // runs no animation frames at all.
    const activeRange = repaint(query, active)
    if (!revealedBySurface && activeRange !== undefined) revealRange(activeRange)

    const observers = listFindSurfaces()
      .map((surface) => {
        const container = surface.getContainer()
        if (container === null) return undefined
        const observer = new MutationObserver(schedule)
        observer.observe(container, { childList: true, subtree: true, characterData: true })
        return observer
      })
      .filter((observer): observer is MutationObserver => observer !== undefined)

    return () => {
      cancelAnimationFrame(frame)
      for (const observer of observers) observer.disconnect()
      clearHighlights()
    }
  }, [open, query, revision, active])
}

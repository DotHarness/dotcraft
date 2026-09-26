import { useEffect, useLayoutEffect, useRef, type RefObject } from 'react'
import {
  loadThreadHistoryGap,
  subscribeHistoryLayoutChange,
  useThreadHistoryStore,
  type HistoryGap,
  type HistoryGapEdge
} from '../stores/threadHistoryStore'

const GAP_PREFETCH_DISTANCE_PX = 800

interface ScrollAnchor {
  turnId: string
  offset: number
}

function turnShells(container: HTMLElement): HTMLElement[] {
  return [...container.querySelectorAll<HTMLElement>('[data-turn-id]')]
}

function captureScrollAnchor(container: HTMLElement): ScrollAnchor | null {
  const top = container.getBoundingClientRect().top
  for (const shell of turnShells(container)) {
    const rect = shell.getBoundingClientRect()
    if (rect.bottom > top) return { turnId: shell.dataset.turnId ?? '', offset: rect.top - top }
  }
  return null
}

function restoreScrollAnchor(container: HTMLElement, anchor: ScrollAnchor): void {
  const shell = turnShells(container).find((node) => node.dataset.turnId === anchor.turnId)
  if (!shell) return
  const drift = shell.getBoundingClientRect().top - container.getBoundingClientRect().top - anchor.offset
  if (drift !== 0) container.scrollTop += drift
}

function requestNearbyGaps(container: HTMLElement, direction: number): void {
  const gaps = useThreadHistoryStore.getState().gaps
  if (gaps.length === 0) return
  const view = container.getBoundingClientRect()
  if (view.height <= 0) return
  for (const node of container.querySelectorAll<HTMLElement>('[data-history-gap]')) {
    const gap = gaps.find((candidate) => String(candidate.id) === node.dataset.historyGap)
    if (!gap) continue
    const rect = node.getBoundingClientRect()
    let edge: HistoryGapEdge | null
    if (rect.bottom <= view.top) {
      edge = direction < 0 && view.top - rect.bottom <= GAP_PREFETCH_DISTANCE_PX ? 'older' : null
    } else if (rect.top >= view.bottom) {
      edge = direction > 0 && rect.top - view.bottom <= GAP_PREFETCH_DISTANCE_PX ? 'newer' : null
    } else {
      edge = rect.top + rect.bottom < view.top + view.bottom ? 'older' : 'newer'
    }
    if (!edge) continue
    void loadThreadHistoryGap(gap.id, edge === 'newer' && gap.newerCursor === null ? 'older' : edge)
      .catch((err: unknown) => { console.error('thread history page load failed:', err) })
  }
}

export function useHistoryGapLoading(
  scrollRef: RefObject<HTMLDivElement | null>,
  gaps: readonly HistoryGap[],
  turnCount: number
): void {
  const anchorRef = useRef<ScrollAnchor | null>(null)

  useEffect(() => subscribeHistoryLayoutChange(() => {
    const container = scrollRef.current
    anchorRef.current = container ? captureScrollAnchor(container) : null
  }), [scrollRef])

  useLayoutEffect(() => {
    const container = scrollRef.current
    const anchor = anchorRef.current
    if (!container || !anchor) return
    anchorRef.current = null
    restoreScrollAnchor(container, anchor)
  })

  useEffect(() => {
    const container = scrollRef.current
    if (!container) return
    let lastScrollTop = container.scrollTop
    const onScroll = (): void => {
      const direction = Math.sign(container.scrollTop - lastScrollTop)
      lastScrollTop = container.scrollTop
      requestNearbyGaps(container, direction)
    }
    container.addEventListener('scroll', onScroll, { passive: true })
    return () => container.removeEventListener('scroll', onScroll)
  }, [scrollRef])

  useEffect(() => {
    const container = scrollRef.current
    if (!container || gaps.length === 0) return
    const frame = requestAnimationFrame(() => requestNearbyGaps(container, 0))
    return () => cancelAnimationFrame(frame)
  }, [scrollRef, gaps, turnCount])
}

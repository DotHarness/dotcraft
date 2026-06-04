import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'

const AT_BOTTOM_THRESHOLD = 20 // px from bottom to be considered "at bottom"

interface UseAutoScrollResult {
  scrollRef: React.RefObject<HTMLDivElement | null>
  showScrollButton: boolean
  scrollToBottom: () => void
}

/**
 * Manages scroll behaviour for a streaming message container.
 *
 * - Tracks whether the user is scrolled to (or near) the bottom.
 * - When `isAtBottom` is true, automatically scrolls to bottom when content changes.
 * - When the user manually scrolls up, disables auto-scroll until they return to bottom.
 * - Exposes `showScrollButton` to render a floating "scroll to bottom" affordance.
 */
export function useAutoScroll(contentLength: number): UseAutoScrollResult {
  const scrollRef = useRef<HTMLDivElement | null>(null)
  const isAtBottomRef = useRef(true)
  const [isAtBottom, setIsAtBottom] = useState(true)

  const updateAtBottom = useCallback((next: boolean) => {
    isAtBottomRef.current = next
    setIsAtBottom((current) => current === next ? current : next)
  }, [])

  const scrollToBottom = useCallback(() => {
    const el = scrollRef.current
    if (!el) return
    const nextScrollTop = Math.max(0, el.scrollHeight - el.clientHeight)
    if (Math.abs(el.scrollTop - nextScrollTop) > 1) {
      el.scrollTop = nextScrollTop
    }
    updateAtBottom(true)
  }, [updateAtBottom])

  const syncAtBottomFromElement = useCallback((el: HTMLDivElement) => {
    updateAtBottom(el.scrollTop + el.clientHeight >= el.scrollHeight - AT_BOTTOM_THRESHOLD)
  }, [updateAtBottom])

  // Check scroll position on user scroll
  useEffect(() => {
    const el = scrollRef.current
    if (!el) return

    function handleScroll(): void {
      if (!el) return
      syncAtBottomFromElement(el)
    }

    el.addEventListener('scroll', handleScroll, { passive: true })
    return () => el.removeEventListener('scroll', handleScroll)
  }, [syncAtBottomFromElement])

  // Auto-scroll to bottom when content grows, if already at bottom
  useLayoutEffect(() => {
    if (isAtBottomRef.current) {
      scrollToBottom()
    }
  }, [contentLength, scrollToBottom])

  // Typewriter text, image loads, and expanded cards can change rendered height
  // without changing the coarse contentLength signal.
  useEffect(() => {
    const el = scrollRef.current
    if (!el || typeof ResizeObserver === 'undefined') return

    const target = el.firstElementChild ?? el
    const observer = new ResizeObserver(() => {
      if (isAtBottomRef.current) {
        scrollToBottom()
      }
    })
    observer.observe(target)
    return () => observer.disconnect()
  }, [scrollToBottom])

  return {
    scrollRef,
    showScrollButton: !isAtBottom,
    scrollToBottom
  }
}

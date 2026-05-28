import { useCallback, useEffect, useRef, useState } from 'react'

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
  const [isAtBottom, setIsAtBottom] = useState(true)

  const scrollToBottom = useCallback(() => {
    const el = scrollRef.current
    if (!el) return
    if (el.scrollTop !== el.scrollHeight) {
      el.scrollTop = el.scrollHeight
    }
    setIsAtBottom((current) => current ? current : true)
  }, [])

  // Check scroll position on user scroll
  useEffect(() => {
    const el = scrollRef.current
    if (!el) return

    function handleScroll(): void {
      if (!el) return
      const atBottom = el.scrollTop + el.clientHeight >= el.scrollHeight - AT_BOTTOM_THRESHOLD
      setIsAtBottom((current) => current === atBottom ? current : atBottom)
    }

    el.addEventListener('scroll', handleScroll, { passive: true })
    return () => el.removeEventListener('scroll', handleScroll)
  }, [])

  // Auto-scroll to bottom when content grows, if already at bottom
  useEffect(() => {
    if (isAtBottom) {
      scrollToBottom()
    }
  }, [contentLength, isAtBottom, scrollToBottom])

  return {
    scrollRef,
    showScrollButton: !isAtBottom,
    scrollToBottom
  }
}

import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'

const AT_BOTTOM_THRESHOLD = 20 // px from bottom to be considered "at bottom"

interface UseAutoScrollResult {
  scrollRef: React.RefObject<HTMLDivElement | null>
  showScrollButton: boolean
  scrollToBottom: () => void
}

export function useAutoScroll(contentLength: number): UseAutoScrollResult {
  const scrollRef = useRef<HTMLDivElement | null>(null)
  const isAtBottomRef = useRef(true)
  // Set just before we programmatically move scrollTop. The browser fires the
  // resulting `scroll` event asynchronously, so it must be told apart from a
  // genuine user scroll — see handleScroll.
  const programmaticScrollRef = useRef(false)
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
      // Mark this write so its echoed `scroll` event doesn't get mistaken for the
      // user leaving the bottom. By the time the event arrives, streaming may have
      // grown scrollHeight further, making the position look "not at bottom" — and
      // acting on that would permanently disable auto-scroll mid-stream.
      programmaticScrollRef.current = true
      el.scrollTop = nextScrollTop
    }
    updateAtBottom(true)
  }, [updateAtBottom])

  const syncAtBottomFromElement = useCallback((el: HTMLDivElement) => {
    updateAtBottom(el.scrollTop + el.clientHeight >= el.scrollHeight - AT_BOTTOM_THRESHOLD)
  }, [updateAtBottom])

  useEffect(() => {
    const el = scrollRef.current
    if (!el) return

    function handleScroll(): void {
      if (!el) return
      // Ignore the echo from our own scrollToBottom write — only genuine user
      // scrolls should be able to turn auto-scroll off.
      if (programmaticScrollRef.current) {
        programmaticScrollRef.current = false
        return
      }
      syncAtBottomFromElement(el)
    }

    el.addEventListener('scroll', handleScroll, { passive: true })
    return () => el.removeEventListener('scroll', handleScroll)
  }, [syncAtBottomFromElement])

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

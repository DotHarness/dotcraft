import { useCallback, useEffect, useLayoutEffect, useRef, useState } from 'react'

const AT_BOTTOM_THRESHOLD = 20

interface UseAutoScrollResult {
  scrollRef: React.RefObject<HTMLDivElement | null>
  showScrollButton: boolean
  scrollToBottom: () => void
}

export function useAutoScroll(contentLength: number): UseAutoScrollResult {
  const scrollRef = useRef<HTMLDivElement | null>(null)
  const isAtBottomRef = useRef(true)
  const programmaticScrollTopRef = useRef<number | null>(null)
  const [isAtBottom, setIsAtBottom] = useState(true)

  const updateAtBottom = useCallback((next: boolean) => {
    isAtBottomRef.current = next
    setIsAtBottom(next)
  }, [])

  const scrollToBottom = useCallback(() => {
    const el = scrollRef.current
    if (!el) return
    const nextScrollTop = Math.max(0, el.scrollHeight - el.clientHeight)
    if (Math.abs(el.scrollTop - nextScrollTop) > 1) {
      el.scrollTop = nextScrollTop
      programmaticScrollTopRef.current = el.scrollTop
    }
    updateAtBottom(true)
  }, [updateAtBottom])

  useEffect(() => {
    const el = scrollRef.current
    if (!el) return

    const handleScroll = (): void => {
      const programmaticTop = programmaticScrollTopRef.current
      programmaticScrollTopRef.current = null
      // Ignore delayed echoes even after content grows, but not a user's newer position.
      if (programmaticTop !== null && Math.abs(el.scrollTop - programmaticTop) <= 1) return
      updateAtBottom(el.scrollTop + el.clientHeight >= el.scrollHeight - AT_BOTTOM_THRESHOLD)
    }

    el.addEventListener('scroll', handleScroll, { passive: true })
    return () => el.removeEventListener('scroll', handleScroll)
  }, [updateAtBottom])

  useLayoutEffect(() => {
    if (isAtBottomRef.current) {
      scrollToBottom()
    }
  }, [contentLength, scrollToBottom])

  // Typewriter text, image loads, and expanded cards can change rendered height
  // without changing the coarse contentLength signal.
  useEffect(() => {
    const el = scrollRef.current
    if (!el) return

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

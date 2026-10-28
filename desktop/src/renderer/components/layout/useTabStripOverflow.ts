import { useEffect, useLayoutEffect, useRef, useState, type RefObject } from 'react'

const REVEAL_INSET = 16

function revealTab(scroller: HTMLElement, key: string | null, behavior: ScrollBehavior): void {
  const tab = key ? scroller.querySelector<HTMLElement>(`[data-tab-key="${key}"]`) : null
  if (!tab) return
  const left = tab.getBoundingClientRect().left - scroller.getBoundingClientRect().left + scroller.scrollLeft
  const right = left + tab.offsetWidth
  if (left - REVEAL_INSET < scroller.scrollLeft) {
    scroller.scrollTo({ left: Math.max(0, left - REVEAL_INSET), behavior })
  } else if (right + REVEAL_INSET > scroller.scrollLeft + scroller.clientWidth) {
    scroller.scrollTo({ left: right + REVEAL_INSET - scroller.clientWidth, behavior })
  }
}

export function useTabStripOverflow(
  scrollerRef: RefObject<HTMLElement | null>,
  activeKey: string | null,
  layoutKey: string
): { start: boolean; end: boolean } {
  const [edges, setEdges] = useState({ start: false, end: false })
  const activeKeyRef = useRef(activeKey)
  activeKeyRef.current = activeKey

  useLayoutEffect(() => {
    const scroller = scrollerRef.current
    if (!scroller) return
    const update = (): void => {
      const start = scroller.scrollLeft > 1
      const end = scroller.scrollWidth - scroller.clientWidth - scroller.scrollLeft > 1
      setEdges((current) => (current.start === start && current.end === end ? current : { start, end }))
    }
    const handleResize = (): void => {
      revealTab(scroller, activeKeyRef.current, 'auto')
      update()
    }
    const handleWheel = (event: WheelEvent): void => {
      if (Math.abs(event.deltaX) >= Math.abs(event.deltaY)) return
      if (scroller.scrollWidth <= scroller.clientWidth) return
      event.preventDefault()
      scroller.scrollLeft += event.deltaY
    }
    const observer = new ResizeObserver(handleResize)
    observer.observe(scroller)
    for (const child of Array.from(scroller.children)) observer.observe(child)
    scroller.addEventListener('scroll', update, { passive: true })
    scroller.addEventListener('wheel', handleWheel, { passive: false })
    return () => {
      observer.disconnect()
      scroller.removeEventListener('scroll', update)
      scroller.removeEventListener('wheel', handleWheel)
    }
  }, [scrollerRef, layoutKey])

  useEffect(() => {
    if (scrollerRef.current) revealTab(scrollerRef.current, activeKey, 'smooth')
  }, [scrollerRef, activeKey])

  return edges
}

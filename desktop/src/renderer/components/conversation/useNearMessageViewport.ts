import { useEffect, useState, type RefObject } from 'react'

export function useNearMessageViewport(ref: RefObject<HTMLElement | null>, watching: boolean): boolean {
  const [near, setNear] = useState(false)
  useEffect(() => {
    if (!watching || near) return
    const element = ref.current
    if (!element) return
    if (typeof IntersectionObserver === 'undefined') {
      setNear(true)
      return
    }
    const root = element.closest<HTMLElement>('[data-testid="message-stream"]')
    const observer = new IntersectionObserver(entries => {
      if (!entries.some(entry => entry.isIntersecting)) return
      observer.disconnect()
      setNear(true)
    }, { root, rootMargin: '320px 0px', threshold: 0.01 })
    observer.observe(element)
    return () => observer.disconnect()
  }, [near, ref, watching])
  return near
}

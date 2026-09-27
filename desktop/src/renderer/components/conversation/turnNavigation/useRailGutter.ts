import { useEffect, useState, type RefObject } from 'react'

const MIN_GUTTER_PX = 48

function hasRoomForRail(scrollEl: HTMLElement, column: HTMLElement): boolean {
  const area = scrollEl.getBoundingClientRect()
  const zoom = scrollEl.offsetWidth > 0 ? area.width / scrollEl.offsetWidth : 1
  return (column.getBoundingClientRect().left - area.left) / (zoom > 0 ? zoom : 1) >= MIN_GUTTER_PX
}

export function useRailGutter(
  scrollRef: RefObject<HTMLDivElement | null>,
  columnRef: RefObject<HTMLDivElement | null>
): boolean {
  const [roomy, setRoomy] = useState(false)

  useEffect(() => {
    const scrollEl = scrollRef.current
    const column = columnRef.current
    if (!scrollEl || !column) return
    let frame: number | null = null
    const measure = (): void => {
      frame ??= requestAnimationFrame(() => {
        frame = null
        setRoomy(hasRoomForRail(scrollEl, column))
      })
    }
    const resizes = new ResizeObserver(measure)
    resizes.observe(scrollEl)
    resizes.observe(column)
    const styles = new MutationObserver(measure)
    styles.observe(scrollEl, { attributes: true, attributeFilter: ['style'] })
    window.addEventListener('resize', measure)
    measure()
    return () => {
      if (frame !== null) cancelAnimationFrame(frame)
      resizes.disconnect()
      styles.disconnect()
      window.removeEventListener('resize', measure)
    }
  }, [columnRef, scrollRef])

  return roomy
}

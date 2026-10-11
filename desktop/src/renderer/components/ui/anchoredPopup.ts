import { useEffect, type RefObject } from 'react'

export interface PopupAnchor {
  x: number
  y: number
  width: number
  height: number
}

export function useDismissOnViewportChange(popupRef: RefObject<HTMLElement | null>, dismiss: (() => void) | null): void {
  useEffect(() => {
    if (!dismiss) return
    const onScroll = (event: Event): void => {
      if (!(event.target instanceof Node && popupRef.current?.contains(event.target))) dismiss()
    }
    window.addEventListener('resize', dismiss)
    window.addEventListener('scroll', onScroll, true)
    return () => {
      window.removeEventListener('resize', dismiss)
      window.removeEventListener('scroll', onScroll, true)
    }
  }, [dismiss, popupRef])
}

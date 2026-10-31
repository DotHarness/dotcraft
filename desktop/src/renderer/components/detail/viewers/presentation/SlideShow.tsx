import { useLayoutEffect, useMemo, useRef, useState, type RefObject } from 'react'
import { createPortal } from 'react-dom'
import type { ReadPresentationResult } from '../../../../../shared/viewer/presentation'
import { useT } from '../../../../contexts/LocaleContext'
import { SlideCanvas } from './SlideCanvas'
import { fitScale, showOrder, slideshowSlideForKey } from './slideNavigation'

interface SlideShowProps {
  deck: ReadPresentationResult
  start: number
  rootRef: RefObject<HTMLDivElement | null>
  onExit: (index: number) => void
}

export function SlideShow({ deck, start, rootRef: ref, onExit }: SlideShowProps): JSX.Element {
  const t = useT()
  const order = useMemo(() => showOrder(deck.slides, start), [deck.slides, start])
  const [position, setPosition] = useState(() => Math.max(0, order.indexOf(start)))
  const [viewport, setViewport] = useState({ width: window.innerWidth, height: window.innerHeight })
  const current = order[position] ?? start
  const latest = useRef(current)
  latest.current = current

  useLayoutEffect(() => {
    const element = ref.current
    if (!element) return
    element.focus({ preventScroll: true })
    let entered = false
    const sync = () => {
      if (document.fullscreenElement === element) entered = true
      else if (entered) onExit(latest.current)
    }
    document.addEventListener('fullscreenchange', sync)
    const measure = () => setViewport({ width: element.clientWidth, height: element.clientHeight })
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => {
      document.removeEventListener('fullscreenchange', sync)
      observer.disconnect()
      if (document.fullscreenElement === element) void document.exitFullscreen().catch(() => undefined)
    }
  }, [ref, onExit])

  const count = order.length
  const label = t('viewer.slides.position', { current: position + 1, total: count })
  const go = (next: number) => setPosition(Math.min(count - 1, Math.max(0, next)))
  return createPortal(
    <div
      ref={ref}
      className="dc-deck-show"
      role="dialog"
      aria-modal="true"
      aria-label={label}
      tabIndex={-1}
      onKeyDown={(event) => {
        event.stopPropagation()
        if (event.key === 'Escape') {
          event.preventDefault()
          onExit(current)
          return
        }
        const next = slideshowSlideForKey(event.key, position, count)
        if (next === null) return
        event.preventDefault()
        go(next)
      }}
      onClick={() => go(position + 1)}
      onContextMenu={(event) => {
        event.preventDefault()
        go(position - 1)
      }}
    >
      <div className="dc-deck-show__slide">
        <SlideCanvas slide={deck.slides[current]!} width={deck.width} height={deck.height} scale={fitScale(viewport, deck, 0)} images={deck.images} />
      </div>
      <span className="dc-deck-show__position" aria-hidden>{label}</span>
    </div>,
    document.body
  )
}

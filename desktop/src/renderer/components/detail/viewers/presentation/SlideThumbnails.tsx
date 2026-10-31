import { useEffect, useRef } from 'react'
import { EyeOff, MessageSquareText } from 'lucide-react'
import type { ReadPresentationResult } from '../../../../../shared/viewer/presentation'
import { useT } from '../../../../contexts/LocaleContext'
import { SlideCanvas } from './SlideCanvas'

const RAIL_THUMB_WIDTH = 112
const GRID_THUMB_WIDTH = 200

interface ThumbnailsProps {
  deck: ReadPresentationResult
  current: number
  counts: Map<number, number>
  onOpen: (index: number) => void
}

function Thumbnail({ deck, index, current, count, width, onOpen }: {
  deck: ReadPresentationResult
  index: number
  current: boolean
  count: number
  width: number
  onOpen: (index: number) => void
}): JSX.Element {
  const t = useT()
  const ref = useRef<HTMLButtonElement>(null)
  const slide = deck.slides[index]!
  const scale = width / deck.width
  useEffect(() => {
    if (current) ref.current?.scrollIntoView({ block: 'nearest', inline: 'nearest' })
  }, [current])
  const label = [
    t('viewer.slides.slide', { number: slide.number }),
    slide.hidden ? t('viewer.slides.hidden') : null,
    count ? t('viewer.slides.comments', { count }) : null
  ].filter(Boolean).join(', ')
  return (
    <li className="dc-deck__thumb-item">
      <button
        ref={ref}
        type="button"
        className="dc-deck__thumb"
        data-hidden={slide.hidden || undefined}
        aria-current={current ? 'true' : undefined}
        aria-label={label}
        tabIndex={current ? 0 : -1}
        onClick={() => onOpen(index)}
      >
        <span className="dc-deck__thumb-number" aria-hidden>
          {slide.number}
          {slide.hidden && <EyeOff size={11} />}
        </span>
        <span className="dc-deck__thumb-frame">
          <SlideCanvas slide={slide} width={deck.width} height={deck.height} scale={scale} images={deck.images} compact />
          {count > 0 && (
            <span className="dc-deck__thumb-count" aria-hidden>
              <MessageSquareText size={10} strokeWidth={2} />
              {count}
            </span>
          )}
        </span>
      </button>
    </li>
  )
}

export function SlideRail({ deck, current, counts, onOpen }: ThumbnailsProps): JSX.Element {
  const t = useT()
  return (
    <nav className="dc-deck__rail" aria-label={t('viewer.slides.list')}>
      <ol className="dc-deck__rail-list">
        {deck.slides.map((slide, index) => (
          <Thumbnail
            key={slide.number}
            deck={deck}
            index={index}
            current={index === current}
            count={counts.get(slide.number) ?? 0}
            width={RAIL_THUMB_WIDTH}
            onOpen={onOpen}
          />
        ))}
      </ol>
    </nav>
  )
}

export function SlideGrid({ deck, current, counts, onOpen }: ThumbnailsProps): JSX.Element {
  const t = useT()
  return (
    <div className="dc-deck__grid-scroll">
      <ol className="dc-deck__grid" aria-label={t('viewer.slides.list')}>
        {deck.slides.map((slide, index) => (
          <Thumbnail
            key={slide.number}
            deck={deck}
            index={index}
            current={index === current}
            count={counts.get(slide.number) ?? 0}
            width={GRID_THUMB_WIDTH}
            onOpen={onOpen}
          />
        ))}
      </ol>
    </div>
  )
}

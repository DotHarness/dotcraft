import { memo } from 'react'
import { ImageOff } from 'lucide-react'
import type { PresentationSlide } from '../../../../../shared/viewer/presentation'
import { useT } from '../../../../contexts/LocaleContext'
import { SlideElementView } from './SlideElementView'
import { fillStyle } from './slideStyle'

interface SlideCanvasProps {
  slide: PresentationSlide
  width: number
  height: number
  scale: number
  images: Record<string, string>
  compact?: boolean
}

export const SlideCanvas = memo(function SlideCanvas({ slide, width, height, scale, images, compact }: SlideCanvasProps): JSX.Element {
  const t = useT()
  if (slide.failed) {
    return (
      <div className="dc-slide__undrawn" style={{ width: width * scale, height: height * scale }}>
        <ImageOff size={compact ? 14 : 20} aria-hidden />
        {!compact && <span>{t('viewer.slides.undrawn')}</span>}
      </div>
    )
  }
  return (
    <div className="dc-slide__clip" style={{ width: width * scale, height: height * scale }}>
      <div
        className="dc-slide"
        style={{ width, height, transform: `scale(${scale})`, ...fillStyle(slide.background, images) }}
      >
        {slide.elements.map((element) => (
          <SlideElementView key={element.id} element={element} images={images} />
        ))}
      </div>
    </div>
  )
})

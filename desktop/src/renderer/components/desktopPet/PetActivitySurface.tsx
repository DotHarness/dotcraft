import { useEffect, useRef, useState } from 'react'
import { PetActivityPill, type PetActivityPillProps } from './PetActivityPill'
import { placePetPill, type PetPillAnchor } from './petPillPlacement'

interface PetActivitySurfaceProps extends PetActivityPillProps {
  position: { x: number; y: number; size: number }
  /** Measured pill height, so the main process can keep room for it below the pet. */
  onLayout: (height: number) => void
}

export function PetActivitySurface({ position, onLayout, ...pill }: PetActivitySurfaceProps): JSX.Element {
  const ref = useRef<HTMLDivElement>(null)
  const [height, setHeight] = useState(0)
  const anchor = useRef<PetPillAnchor>('below')
  const layout = useRef(onLayout)
  layout.current = onLayout
  const placement = placePetPill({
    position,
    viewport: { width: window.innerWidth, height: window.innerHeight },
    height,
    previous: anchor.current
  })
  anchor.current = placement.anchor

  useEffect(() => {
    const element = ref.current
    if (!element) return undefined
    const measure = (): void => {
      const next = Math.round(element.getBoundingClientRect().height)
      setHeight((current) => current === next ? current : next)
    }
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [])
  useEffect(() => { layout.current(height) }, [height])
  useEffect(() => () => layout.current(0), [])

  return <div className="desktop-pet-pill-position" data-pet-interactive data-anchor={placement.anchor}
    style={{ left: placement.left, top: placement.top, width: placement.width, maxHeight: placement.maxHeight }}>
    <div ref={ref} className="desktop-pet-pill-measure">
      <PetActivityPill {...pill} />
    </div>
  </div>
}

import { useLayoutEffect, useRef, type ReactNode } from 'react'
import type { PresentationSlide } from '../../../../../shared/viewer/presentation'
import type { PreviewBox } from '../PreviewComments'
import { useWheelZoom } from '../viewerZoom'
import { SlideCanvas } from './SlideCanvas'
import type { SlideSelection } from './useSlideComments'

export interface StageHighlight {
  key: string
  box: PreviewBox
  active: boolean
}

interface SlideStageProps {
  slide: PresentationSlide
  width: number
  height: number
  images: Record<string, string>
  scale: number
  padding: number
  label: string
  slideSelected: boolean
  selectionBox: PreviewBox | null
  highlights: StageHighlight[]
  overlays: ReactNode[]
  onViewport: (size: { width: number; height: number }) => void
  onZoom: (update: (current: number) => number) => void
  onSelect: (selection: SlideSelection | null) => void
  onLink: (url: string) => void
  focusRef: (element: HTMLDivElement | null) => void
}

export function SlideStage(props: SlideStageProps): JSX.Element {
  const { slide, width, height, scale, padding, onViewport } = props
  const stageRef = useRef<HTMLDivElement>(null)

  useLayoutEffect(() => {
    const element = stageRef.current
    if (!element) return
    const measure = () => onViewport({ width: element.clientWidth, height: element.clientHeight })
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(element)
    return () => observer.disconnect()
  }, [onViewport])

  useWheelZoom(stageRef, props.onZoom)

  return (
    <div
      ref={(element) => {
        stageRef.current = element
        props.focusRef(element)
      }}
      className="dc-deck__stage"
      tabIndex={0}
      role="region"
      aria-label={props.label}
      onPointerDown={(event) => {
        if (event.button !== 0) return
        const target = event.target instanceof Element ? event.target : null
        stageRef.current?.focus({ preventScroll: true })
        if (!target?.closest('.dc-deck__frame')) {
          props.onSelect(null)
          return
        }
        const elementId = target.closest<HTMLElement>('[data-element-id]')?.dataset.elementId ?? null
        props.onSelect(slide.failed ? { slide: slide.number, elementId: null } : { slide: slide.number, elementId })
      }}
      onClick={(event) => {
        const link = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-link]')?.dataset.link : undefined
        if (link) props.onLink(link)
      }}
    >
      <div className="dc-deck__canvas" style={{ width: width * scale + padding * 2, height: height * scale + padding * 2 }}>
        <div
          className="dc-deck__frame"
          data-selected={props.slideSelected || undefined}
          style={{ top: padding, left: padding, width: width * scale, height: height * scale }}
        >
          <SlideCanvas slide={slide} width={width} height={height} scale={scale} images={props.images} />
        </div>
        {props.highlights.map((highlight) => (
          <div key={highlight.key} className="dc-deck__match" data-active={highlight.active || undefined} style={highlight.box} />
        ))}
        {props.selectionBox && <div className="dc-deck__selection" style={props.selectionBox} />}
        {props.overlays}
      </div>
    </div>
  )
}

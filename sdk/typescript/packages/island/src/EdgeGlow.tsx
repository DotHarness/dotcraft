import { useLayoutEffect, useRef, useState, type CSSProperties } from 'react'
import { glowGeometry, type GlowPoint } from './index.js'

export interface EdgeGlowProps {
  /** The island's centre in this surface's pixels. */
  origin?: GlowPoint | null
  reduceMotion?: boolean
  className?: string
}

/** Each comet is drawn as strokes of falling length sharing one head, which reads as a fading tail. */
const TAIL = [
  { length: 360, width: 20, opacity: 0.06 },
  { length: 260, width: 13, opacity: 0.1 },
  { length: 180, width: 8, opacity: 0.18 },
  { length: 110, width: 4, opacity: 0.4 },
  { length: 48, width: 2, opacity: 1 },
]

export function EdgeGlow({ origin = null, reduceMotion = false, className }: EdgeGlowProps) {
  const element = useRef<HTMLDivElement | null>(null)
  const [size, setSize] = useState({ width: 0, height: 0 })

  useLayoutEffect(() => {
    const node = element.current
    if (!node) return undefined
    const measure = () => setSize({ width: node.clientWidth, height: node.clientHeight })
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(node)
    return () => observer.disconnect()
  }, [])

  const { width, height } = size
  const { edge, path, meetAt } = glowGeometry(width, height, origin)
  const loop = 2 * (Math.max(0, width - 2) + Math.max(0, height - 2))
  const style = {
    '--dci-glow-x': `${edge.x}px`,
    '--dci-glow-y': `${edge.y}px`,
    '--dci-glow-loop': `${loop}px`,
    '--dci-glow-meet': `${loop * meetAt}px`,
  } as CSSProperties

  return (
    <div ref={element} className={className ? `dci-glow ${className}` : 'dci-glow'} data-reduce-motion={reduceMotion ? 'on' : 'off'} style={style} aria-hidden="true">
      <div className="dci-glow__rim" />
      <div className="dci-glow__source" />
      {!reduceMotion && width > 0 && (
        <svg className="dci-glow__comets" width={width} height={height} viewBox={`0 0 ${width} ${height}`}>
          {(['forward', 'back'] as const).map((direction) => (
            <g key={direction} className="dci-glow__comet" data-direction={direction}>
              {TAIL.map((stroke) => (
                <path
                  key={stroke.length}
                  d={path}
                  style={{ '--dci-glow-tail': `${stroke.length}px`, strokeWidth: stroke.width, strokeOpacity: stroke.opacity, strokeDasharray: `${stroke.length}px ${loop}px` } as CSSProperties}
                />
              ))}
            </g>
          ))}
        </svg>
      )}
    </div>
  )
}

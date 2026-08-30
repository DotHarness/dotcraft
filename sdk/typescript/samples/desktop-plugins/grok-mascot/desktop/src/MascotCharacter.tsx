import { useEffect, useId, useRef, type CSSProperties, type JSX } from 'react'
import {
  CENTER,
  COLORS,
  VIEWBOX,
  personaShapePath,
  resolvePersonaColor,
  resolvePersonaShape
} from './characterArt'
import { MOTION, type CharacterState } from './characterMotion'

/** Knockout eyes need an opaque fill: every surface token resolves through color-mix with transparent. */
const EYE_FILL = 'var(--bg-primary, #141515)'

export interface MascotCharacterProps {
  readonly sizePx: number
  readonly state: CharacterState
  readonly color?: string | null
  readonly shape?: string | null
  readonly sourceId?: string
  readonly followPointer?: boolean
  readonly reducedMotion?: boolean
  readonly paused?: boolean
  readonly className?: string
}

export function MascotCharacter({
  sizePx,
  state,
  color = null,
  shape = null,
  sourceId = 'dotcraft',
  followPointer = false,
  reducedMotion,
  paused = false,
  className
}: MascotCharacterProps): JSX.Element {
  const id = useId().replace(/:/g, '')
  const faceRef = useRef<SVGGElement>(null)
  const eyesRef = useRef<SVGGElement>(null)
  const gazeRef = useRef({ x: 0, y: 0 })
  const colors = COLORS[resolvePersonaColor(sourceId, color)] ?? COLORS.black
  const motion = MOTION[state] ?? MOTION.idle
  const still = reducedMotion ?? prefersReducedMotion()

  useEffect(() => {
    if (!followPointer || still || typeof window === 'undefined') return
    const handlePointerMove = (event: PointerEvent): void => {
      const rect = faceRef.current?.ownerSVGElement?.getBoundingClientRect()
      if (rect == null || rect.width === 0 || rect.height === 0) return
      gazeRef.current = {
        x: Math.max(-1, Math.min(1, (event.clientX - (rect.left + rect.width / 2)) / (rect.width / 2))),
        y: Math.max(-1, Math.min(1, (event.clientY - (rect.top + rect.height / 2)) / (rect.height / 2)))
      }
    }
    const clearPointer = (): void => {
      gazeRef.current = { x: 0, y: 0 }
    }
    window.addEventListener('pointermove', handlePointerMove, { passive: true })
    document.documentElement.addEventListener('pointerleave', clearPointer)
    return () => {
      window.removeEventListener('pointermove', handlePointerMove)
      document.documentElement.removeEventListener('pointerleave', clearPointer)
      clearPointer()
    }
  }, [followPointer, still])

  useEffect(() => {
    const face = faceRef.current
    const eyes = eyesRef.current
    if (face == null || eyes == null || still || paused) return
    let frame = 0
    const started = performance.now()
    const tick = (time: number): void => {
      const phase = ((time - started) / motion.period) * Math.PI * 2
      const bob = Math.sin(phase) * motion.amplitude
      const gaze = gazeRef.current
      face.setAttribute('transform', `translate(0 ${-bob}) rotate(${motion.tilt} ${CENTER} ${CENTER})`)
      eyes.setAttribute('transform', `translate(${gaze.x * 4} ${gaze.y * 3}) scale(1 ${motion.eye})`)
      frame = requestAnimationFrame(tick)
    }
    frame = requestAnimationFrame(tick)
    return () => cancelAnimationFrame(frame)
  }, [motion, paused, still])

  const rootStyle: CSSProperties = {
    display: 'block',
    height: sizePx,
    overflow: 'visible',
    userSelect: 'none',
    WebkitUserSelect: 'none',
    width: sizePx
  }
  const eyeHeight = state === 'sleeping' ? 2 : 7

  return (
    <svg
      aria-hidden="true"
      className={className}
      data-state={state}
      data-paused={paused || undefined}
      data-reduced-motion={still ? 'true' : 'false'}
      height={sizePx}
      style={rootStyle}
      viewBox={VIEWBOX}
      width={sizePx}
      xmlns="http://www.w3.org/2000/svg"
    >
      <defs>
        <linearGradient id={`${id}-ink`} x1="0" x2="1" y1="0" y2="1">
          <stop offset="0" stopColor={colors.light} />
          <stop offset="1" stopColor={colors.dark} />
        </linearGradient>
      </defs>
      <g ref={faceRef} transform="translate(0 0)">
        <path d={personaShapePath(resolvePersonaShape(sourceId, shape))} fill={`url(#${id}-ink)`} />
        <g ref={eyesRef} fill={EYE_FILL} transform="translate(0 0)">
          <ellipse cx={CENTER - 29} cy={CENTER - 8} rx="10" ry={eyeHeight} />
          <ellipse cx={CENTER + 29} cy={CENTER - 8} rx="10" ry={eyeHeight} />
        </g>
      </g>
    </svg>
  )
}

function prefersReducedMotion(): boolean {
  return (
    typeof window !== 'undefined' &&
    window.matchMedia?.('(prefers-reduced-motion: reduce)').matches === true
  )
}

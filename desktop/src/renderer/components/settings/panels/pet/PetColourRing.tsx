import { useRef, useState, type CSSProperties, type JSX, type MouseEvent } from 'react'
import { DEFAULT_MASCOT_PALETTE, PALETTE } from '@dotcraft/avatar'
import type { MessageKey } from '../../../../../shared/locales'
import { useT } from '../../../../contexts/LocaleContext'

const COLOURS: Array<{ value: number; key: MessageKey; color: string }> = [
  { value: -1, key: 'settings.pet.colour.original', color: DEFAULT_MASCOT_PALETTE.bodyM },
  ...PALETTE.map((entry, value) => ({ value, key: `settings.pet.colour.${entry.key}` as MessageKey, color: entry.bodyM }))
]
export function colourKey(palette: number): MessageKey {
  return COLOURS.find((entry) => entry.value === palette)!.key
}

const RING_SIZE = 232
const CENTER = RING_SIZE / 2
const SWEEP = 300
const START = 120
const STEP = SWEEP / COLOURS.length
const GAP = 2.4
const OUTER = 112
const INNER = 101
const POP = 5
const HIT_INNER = 76
const HIT_OUTER = OUTER + 14
const GROW_INWARD = 12
const GROW_OUTWARD = 3

function coords(radius: number, degrees: number): [number, number] {
  const rad = degrees * Math.PI / 180
  return [CENTER + radius * Math.cos(rad), CENTER + radius * Math.sin(rad)]
}
function arc(outer: number, inner: number, a0: number, a1: number): string {
  const large = a1 - a0 > 180 ? 1 : 0
  const [x0, y0] = coords(outer, a0), [x1, y1] = coords(outer, a1), [x2, y2] = coords(inner, a1), [x3, y3] = coords(inner, a0)
  return `M ${x0.toFixed(2)} ${y0.toFixed(2)} A ${outer} ${outer} 0 ${large} 1 ${x1.toFixed(2)} ${y1.toFixed(2)} L ${x2.toFixed(2)} ${y2.toFixed(2)} A ${inner} ${inner} 0 ${large} 0 ${x3.toFixed(2)} ${y3.toFixed(2)} Z`
}
const BAND = arc(HIT_OUTER, HIT_INNER, START, START + SWEEP)

function segmentAt(event: MouseEvent<SVGPathElement>): number | null {
  const rect = event.currentTarget.ownerSVGElement!.getBoundingClientRect()
  const scale = RING_SIZE / rect.width
  const x = (event.clientX - rect.left) * scale - CENTER
  const y = (event.clientY - rect.top) * scale - CENTER
  const radius = Math.hypot(x, y)
  if (radius < HIT_INNER || radius > HIT_OUTER) return null
  const angle = ((Math.atan2(y, x) * 180 / Math.PI - START) % 360 + 360) % 360
  return angle < SWEEP ? Math.floor(angle / STEP) : null
}

interface PetColourRingProps {
  value: number
  onChange: (palette: number) => void
  onPreview: (palette: number | null) => void
}

export function PetColourRing({ value, onChange, onPreview }: PetColourRingProps): JSX.Element {
  const t = useT()
  const [pointed, setPointed] = useState<number | null>(null)
  const [focused, setFocused] = useState<number | null>(null)
  const pointedRef = useRef<number | null>(null)
  const point = (index: number | null): void => {
    if (pointedRef.current === index) return
    pointedRef.current = index
    setPointed(index)
    onPreview(index === null ? null : COLOURS[index]!.value)
  }
  const target = pointed ?? focused
  return (
    <svg className="pet-settings-ring" viewBox={`0 0 ${RING_SIZE} ${RING_SIZE}`} role="radiogroup" aria-label={t('settings.pet.colour.label')}>
      {COLOURS.map((entry, index) => {
        const a0 = START + index * STEP + GAP / 2
        const a1 = START + (index + 1) * STEP - GAP / 2
        const selected = entry.value === value
        const swell = index === target ? 1 : 0
        const outer = OUTER + Math.max(selected ? POP : 0, GROW_OUTWARD * swell)
        const inner = INNER - Math.max(selected ? 2 : 0, GROW_INWARD * swell)
        const d = arc(outer, inner, a0, a1)
        const [markX, markY] = coords((outer + inner) / 2, (a0 + a1) / 2)
        return (
          <g key={entry.value} className="pet-settings-ring-segment" data-selected={selected} data-target={index === target}>
            <path d={d} style={{ d: `path('${d}')` } as CSSProperties} fill={entry.color} role="radio" aria-checked={selected} aria-label={t(entry.key)} tabIndex={0}
              onClick={() => onChange(entry.value)}
              onKeyDown={(event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onChange(entry.value) } }}
              onFocus={() => { setFocused(index); if (pointedRef.current === null) onPreview(entry.value) }}
              onBlur={() => { setFocused(null); if (pointedRef.current === null) onPreview(null) }} />
            {selected && <circle className="pet-settings-ring-mark" r="3" style={{ transform: `translate(${markX.toFixed(2)}px, ${markY.toFixed(2)}px)` }} />}
          </g>
        )
      })}
      <path className="pet-settings-ring-band" d={BAND} aria-hidden="true"
        onPointerMove={(event) => point(segmentAt(event))}
        onPointerLeave={() => point(null)}
        onClick={(event) => { const index = segmentAt(event); if (index !== null) onChange(COLOURS[index]!.value) }} />
    </svg>
  )
}

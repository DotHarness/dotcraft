import type { JSX } from 'react'
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
const GAP = 2.4
const OUTER = 112
const INNER = 101
const POP = 5

function coords(radius: number, degrees: number): [number, number] {
  const rad = degrees * Math.PI / 180
  return [CENTER + radius * Math.cos(rad), CENTER + radius * Math.sin(rad)]
}
function arc(outer: number, inner: number, a0: number, a1: number): string {
  const [x0, y0] = coords(outer, a0), [x1, y1] = coords(outer, a1), [x2, y2] = coords(inner, a1), [x3, y3] = coords(inner, a0)
  return `M ${x0.toFixed(2)} ${y0.toFixed(2)} A ${outer} ${outer} 0 0 1 ${x1.toFixed(2)} ${y1.toFixed(2)} L ${x2.toFixed(2)} ${y2.toFixed(2)} A ${inner} ${inner} 0 0 0 ${x3.toFixed(2)} ${y3.toFixed(2)} Z`
}

interface PetColourRingProps {
  value: number
  onChange: (palette: number) => void
  onPreview: (palette: number | null) => void
}

export function PetColourRing({ value, onChange, onPreview }: PetColourRingProps): JSX.Element {
  const t = useT()
  const step = SWEEP / COLOURS.length
  return (
    <svg className="pet-settings-ring" viewBox={`0 0 ${RING_SIZE} ${RING_SIZE}`} role="radiogroup" aria-label={t('settings.pet.colour.label')}>
      {COLOURS.map((entry, index) => {
        const a0 = START + index * step + GAP / 2
        const a1 = START + (index + 1) * step - GAP / 2
        const selected = entry.value === value
        const outer = selected ? OUTER + POP : OUTER
        const inner = selected ? INNER - 2 : INNER
        const [markX, markY] = coords((outer + inner) / 2, (a0 + a1) / 2)
        return (
          <g key={entry.value} className="pet-settings-ring-segment" data-selected={selected}>
            <path d={arc(outer, inner, a0, a1)} fill={entry.color} role="radio" aria-checked={selected} aria-label={t(entry.key)} tabIndex={0}
              onClick={() => onChange(entry.value)}
              onKeyDown={(event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); onChange(entry.value) } }}
              onMouseEnter={() => onPreview(entry.value)} onMouseLeave={() => onPreview(null)}
              onFocus={() => onPreview(entry.value)} onBlur={() => onPreview(null)}>
              <title>{t(entry.key)}</title>
            </path>
            {selected && <circle className="pet-settings-ring-mark" cx={markX.toFixed(2)} cy={markY.toFixed(2)} r="3" />}
          </g>
        )
      })}
    </svg>
  )
}

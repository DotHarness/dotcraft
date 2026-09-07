import type { PrimaryId, SecondaryId, DecorationId } from './appearanceModel.js'
import { isHeld } from './appearanceModel.js'
import { HeldDecoration } from './HeldDecorations.js'
import { HatDecoration } from './HatDecorations.js'
import { ObjectDecoration } from './ObjectDecorations.js'
import { decorationOf } from './decorationCatalog.js'

export function SecondaryDecoration({ id }: { id: SecondaryId }) {
  if (isHeld(id)) return <HeldDecoration id={id} />
  switch (id) {
    case 'forehead-goggles': return <g strokeLinejoin="round">
      <path d="M374 419h109v46H374Zm167 0h109v46H541Z" fill="#a2c5d1" stroke="#8b7568" strokeWidth="14" />
      <path d="M483 442q29 20 58 0" stroke="#8b7568" strokeWidth="11" fill="none" />
      <path d="m389 429 18 21m149-21 18 21" stroke="#e0eff1" strokeWidth="8" />
    </g>
    default: return null
  }
}
export function PrimaryDecoration({ id }: { id: PrimaryId }) {
  return <g data-decoration={id}><HatDecoration id={id} /><ObjectDecoration id={id} /></g>
}
export function DecoratedTop({ id }: { id: Exclude<PrimaryId, 'none'> }) {
  return <PrimaryDecoration id={id} />
}
export function DecorationSwatch({ id, size = 112 }: { id: DecorationId; size?: number }) {
  const secondary = decorationOf(id).category === 'Accessories'
  const forehead = id === 'forehead-goggles'
  return <svg width={size} height={size} viewBox={secondary ? forehead ? '300 280 424 324' : '95 635 260 260' : '265 105 494 360'} fill="none" role="img" aria-label={`${decorationOf(id).name} specimen`}>
    {secondary ? <SecondaryDecoration id={id as SecondaryId} /> : <PrimaryDecoration id={id as PrimaryId} />}
  </svg>
}

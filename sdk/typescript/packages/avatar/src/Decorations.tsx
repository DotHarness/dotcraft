import { useId } from 'react'
import type { ItemId, HeadId, SkinId, Zone } from './items.js'
import { itemOf } from './items.js'
import { HatDecoration } from './HatDecorations.js'
import { ObjectDecoration } from './ObjectDecorations.js'
import { FaceDecoration, FaceplateEyes, FaceplatePlate, isFaceplate } from './FaceDecorations.js'
import { HandDecoration } from './HandDecorations.js'
import { BackDecoration, BackFrontDecoration } from './BackDecorations.js'
import { SkinOverlay, SkinPaintSurface, skinPaint } from './SkinDecorations.js'
import { decorationOf } from './decorationCatalog.js'

export function HeadDecoration({ id }: { id: HeadId }) {
  return <g data-decoration={id}><HatDecoration id={id} /><ObjectDecoration id={id} /></g>
}

function SkinSpecimen({ id }: { id: SkinId }) {
  const uid = useId().replace(/:/g, '')
  const paint = skinPaint(id)
  return <g data-skin={id}>
    <defs>
      {paint?.render(`dca-swatch-paint-${uid}`)}
      <clipPath id={`dca-swatch-clip-${uid}`}><rect x="243" y="408" width="538" height="426" rx="113" /></clipPath>
    </defs>
    <rect x="243" y="408" width="538" height="426" rx="113" fill={paint ? `url(#dca-swatch-paint-${uid})` : '#4f7cf6'} stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
    <g clipPath={`url(#dca-swatch-clip-${uid})`}>{paint ? <SkinPaintSurface id={id} /> : <SkinOverlay id={id} />}</g>
  </g>
}

export function SlotDecoration({ id }: { id: ItemId }) {
  const item = itemOf(id)
  switch (item.slot) {
    case 'head': return <HeadDecoration id={item.id} />
    case 'face': return isFaceplate(item.id)
      ? <g data-accessory={item.id}><rect x="295" y="464" width="434" height="315" rx="78" fill="#fff" /><FaceplatePlate id={item.id} /><FaceplateEyes id={item.id} /></g>
      : <g data-accessory={item.id}><FaceDecoration id={item.id} /></g>
    case 'hand': return <g data-accessory={item.id}><HandDecoration id={item.id} /></g>
    case 'back': return <g data-back={item.id}><BackDecoration id={item.id} /><BackFrontDecoration id={item.id} /></g>
    case 'skin': return <SkinSpecimen id={item.id} />
  }
}

const swatchViewBox: Record<ReturnType<typeof itemOf>['slot'], string> = {
  head: '265 105 494 360',
  face: '300 280 424 324',
  hand: '10 330 460 460',
  back: '20 120 984 800',
  skin: '203 368 618 506',
}
export function DecorationSwatch({ id, size = 112 }: { id: ItemId; size?: number }) {
  const item = itemOf(id)
  const zones: readonly Zone[] = item.zones
  const viewBox = zones.includes('screen') ? '255 404 514 436' : zones.includes('rim') ? '346 300 546 590' : swatchViewBox[item.slot]
  return <svg width={size} height={size} viewBox={viewBox} fill="none" role="img" aria-label={`${decorationOf(id).name} specimen`}
    className="dca-swatch dca-part-robot" data-expression="neutral" data-slot={item.slot} data-effects="static">
    <SlotDecoration id={id} />
  </svg>
}

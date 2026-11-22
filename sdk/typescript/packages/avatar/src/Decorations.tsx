import { useId } from 'react'
import type { AnyItemId, HeadId, SkinId, Slot, Zone } from './items.js'
import { isItemId } from './items.js'
import { HatDecoration } from './HatDecorations.js'
import { ObjectDecoration } from './ObjectDecorations.js'
import { CostumeHeadBackDecoration, CostumeHeadDecoration } from './CostumeDecorations.js'
import { FaceDecoration, FaceplateEyes, FaceplatePlate, isFaceplate } from './FaceDecorations.js'
import { HandDecoration } from './HandDecorations.js'
import { BackDecoration, BackFrontDecoration } from './BackDecorations.js'
import { SkinOverlay, SkinPaintSurface, skinPaint } from './SkinDecorations.js'
import { useAvatarCatalog, type AvatarCatalog, type AvatarSkinOverlayComponentItem, type AvatarSkinPaintComponentItem } from './pack.js'

export function HeadDecoration({ id }: { id: HeadId }) {
  return <g data-decoration={id}><HatDecoration id={id} /><ObjectDecoration id={id} /><CostumeHeadDecoration id={id} /></g>
}

export function HeadBackDecoration({ id }: { id: HeadId }) {
  return <CostumeHeadBackDecoration id={id} />
}

function SkinSpecimen({ id, custom }: { id: string; custom?: AvatarSkinOverlayComponentItem | AvatarSkinPaintComponentItem }) {
  const uid = useId().replace(/:/g, '')
  const paintId = `dca-swatch-paint-${uid}`
  const clipId = `dca-swatch-clip-${uid}`
  const paint = custom ? (custom.kind === 'paint' ? { render: custom.renderPaint } : undefined) : skinPaint(id as SkinId)
  return <g data-skin={id}>
    <defs>
      {paint?.render(paintId)}
      <clipPath id={clipId}><rect x="243" y="408" width="538" height="426" rx="113" /></clipPath>
    </defs>
    <rect x="243" y="408" width="538" height="426" rx="113" fill={paint ? `url(#${paintId})` : '#4f7cf6'} stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
    <g clipPath={`url(#${clipId})`}>
      {custom
        ? custom.renderSurface?.()
        : paint ? <SkinPaintSurface id={id as SkinId} /> : <SkinOverlay id={id as SkinId} />}
    </g>
  </g>
}

export function SlotDecoration({ id, catalog }: { id: AnyItemId; catalog?: AvatarCatalog }) {
  const activeCatalog = useAvatarCatalog(catalog)
  const custom = activeCatalog.componentOf(id)
  if (custom) {
    switch (custom.slot) {
      case 'head': return <g data-decoration={id}>{custom.renderBack?.()}{custom.render()}</g>
      case 'face': return custom.kind === 'faceplate'
        ? <g data-accessory={id}><rect x="295" y="464" width="434" height="315" rx="78" fill="#fff" />{custom.renderPlate()}{custom.renderEyes()}</g>
        : <g data-accessory={id}>{custom.render()}</g>
      case 'hand': return <g data-accessory={id}><g data-held-decoration={id}>{custom.render()}</g></g>
      case 'back': return <g data-back={id}>{custom.renderBack()}{custom.renderFront?.()}</g>
      case 'skin': return <SkinSpecimen id={id} custom={custom} />
    }
  }
  if (!isItemId(id)) return null
  const item = activeCatalog.registry.get(id)
  if (!item) return null
  switch (item.slot) {
    case 'head': return <><CostumeHeadBackDecoration id={id as HeadId} /><HeadDecoration id={id as HeadId} /></>
    case 'face': return isFaceplate(id as never)
      ? <g data-accessory={id}><rect x="295" y="464" width="434" height="315" rx="78" fill="#fff" /><FaceplatePlate id={id as never} /><FaceplateEyes id={id as never} /></g>
      : <g data-accessory={id}><FaceDecoration id={id as never} /></g>
    case 'hand': return <g data-accessory={id}><HandDecoration id={id as never} /></g>
    case 'back': return <g data-back={id}><BackDecoration id={id as never} /><BackFrontDecoration id={id as never} /></g>
    case 'skin': return <SkinSpecimen id={id} />
  }
}

const swatchViewBox: Record<Slot, string> = {
  head: '265 105 494 360',
  face: '300 280 424 324',
  hand: '10 330 460 460',
  back: '20 120 984 800',
  skin: '203 368 618 506',
}
export function DecorationSwatch({ id, size = 112, label, catalog }: { id: AnyItemId; size?: number; label?: string; catalog?: AvatarCatalog }) {
  const activeCatalog = useAvatarCatalog(catalog)
  const item = activeCatalog.registry.decorationOf(id)
  if (!item) return <svg width={size} height={size} viewBox="0 0 1024 1024" fill="none" aria-hidden="true" />
  const zones: readonly Zone[] = item.zones
  const viewBox = item.swatchViewBox ?? (zones.includes('screen') ? '255 404 514 436' : zones.includes('rim') ? '346 300 546 590' : swatchViewBox[item.slot])
  return <svg width={size} height={size} viewBox={viewBox} fill="none" role="img" aria-label={label ?? `${item.name} specimen`}
    className="dca-swatch dca-part-robot" data-expression="neutral" data-slot={item.slot} data-effects="static">
    <SlotDecoration id={id} catalog={activeCatalog} />
  </svg>
}

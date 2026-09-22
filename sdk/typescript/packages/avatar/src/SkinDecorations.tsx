import type { ReactNode } from 'react'
import type { SkinId } from './items.js'
import type { BodyPaint } from './MascotRig.js'
import { isPaintSkin, paintMaterials, type PaintSkinId } from './paintMaterials.js'
import { materialBounds } from './RigMaterial.js'

const dots = [[280, 440], [372, 432], [464, 446], [556, 432], [648, 446], [740, 438], [268, 560], [758, 530], [262, 680], [762, 650], [292, 806], [400, 812], [512, 800], [624, 812], [732, 806], [268, 760]]
const star4 = (r: number) => `M0 ${-r}l${r * .28} ${r * .72}l${r * .72} ${r * .28}l${-r * .72} ${r * .28}l${-r * .28} ${r * .72}l${-r * .28} ${-r * .72}l${-r * .72} ${-r * .28}l${r * .72} ${-r * .28}Z`

export function SkinOverlay({ id }: { id: SkinId }) {
  switch (id) {
    case 'stripes': return <g fill="#fff" opacity=".34" data-skin-overlay={id}>
      {[60, 250, 440, 630].map(x => <path key={x} d={`M${x} 834 L${x + 220} 408 h80 L${x + 80} 834Z`} />)}
    </g>
    default: return null
  }
}

const sheen = <g className="dca-fx dca-fx-sheen" fill="#fff">
  <path d="M120 834 L420 408 h58 L178 834Z" opacity=".38" />
  <path d="M200 834 L500 408 h22 L222 834Z" opacity=".55" />
</g>

const gradients: Record<PaintSkinId, (id: string) => ReactNode> = {
  chrome: id => <linearGradient id={id} className="dca-skin-paint dca-skin-chrome" x1="279" y1="766" x2="736" y2="334" gradientUnits="userSpaceOnUse">
    <stop offset="0" stopColor="#3c4658" /><stop offset=".36" stopColor="#9aa6b8" /><stop offset=".5" stopColor="#f4f7fb" />
    <stop offset=".54" stopColor="#6b7688" /><stop offset=".8" stopColor="#c3ccd8" /><stop offset="1" stopColor="#ffffff" />
  </linearGradient>,
  holographic: id => <linearGradient id={id} className="dca-skin-paint dca-skin-holo" x1="279" y1="766" x2="736" y2="334" gradientUnits="userSpaceOnUse">
    <stop offset="0" stopColor="#ff8ad4" /><stop offset=".25" stopColor="#ffd28a" /><stop offset=".5" stopColor="#c9ff8a" />
    <stop offset=".75" stopColor="#8ad8ff" /><stop offset="1" stopColor="#c98aff" />
  </linearGradient>,
  gold: id => <linearGradient id={id} className="dca-skin-paint dca-skin-gold" x1="279" y1="766" x2="736" y2="334" gradientUnits="userSpaceOnUse">
    <stop offset="0" stopColor="#b8862c" /><stop offset=".4" stopColor="#f6d365" /><stop offset=".52" stopColor="#fff3c4" />
    <stop offset=".64" stopColor="#d9a53a" /><stop offset="1" stopColor="#ffe9a8" />
  </linearGradient>,
  lava: id => <linearGradient id={id} className="dca-skin-paint dca-skin-lava" x1="279" y1="766" x2="736" y2="334" gradientUnits="userSpaceOnUse">
    <stop offset="0" stopColor="#4a1208" /><stop offset=".3" stopColor="#e8451f" /><stop offset=".55" stopColor="#ffb347" />
    <stop offset=".8" stopColor="#e8451f" /><stop offset="1" stopColor="#7a1f0e" />
  </linearGradient>,
  galaxy: id => <linearGradient id={id} className="dca-skin-paint dca-skin-galaxy" x1="279" y1="766" x2="736" y2="334" gradientUnits="userSpaceOnUse">
    <stop offset="0" stopColor="#1b1040" /><stop offset=".3" stopColor="#4c1d95" /><stop offset=".55" stopColor="#2563eb" />
    <stop offset=".8" stopColor="#0b3d62" /><stop offset="1" stopColor="#7c3aed" />
  </linearGradient>,
}
export function skinPaint(id: SkinId): BodyPaint | undefined {
  return isPaintSkin(id) ? { render: gradients[id], shadow: paintMaterials[id].shadow } : undefined
}
export function SkinPaintSurface({ id }: { id: SkinId }) {
  if (!isPaintSkin(id)) return null
  return <g data-skin-overlay={id}>
    {id !== 'galaxy' && id !== 'lava' && sheen}
    {id === 'galaxy' && <g fill="#fff">{dots.filter((_, index) => index % 2 === 1).map(([cx, cy], index) => <path key={`${cx}-${cy}`} className="dca-fx dca-fx-node" d={star4(index % 2 ? 10 : 15)} transform={`translate(${cx} ${cy})`} style={{ animationDelay: `${(index * .43) % 1.8}s` }} />)}</g>}
    <rect className="dca-fx dca-skin-energy" {...materialBounds} fill={paintMaterials[id].accent} opacity="0" />
  </g>
}

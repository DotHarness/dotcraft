import type { ReactNode } from 'react'
import type { SkinId } from './items.js'
import type { BodyPaint } from './MascotRig.js'
import { isPaintSkin, paintMaterials, type PaintSkinId } from './paintMaterials.js'
import { materialBounds } from './RigMaterial.js'

const star4 = (r: number) => `M0 ${-r}l${r * .28} ${r * .72}l${r * .72} ${r * .28}l${-r * .72} ${r * .28}l${-r * .28} ${r * .72}l${-r * .28} ${-r * .72}l${-r * .72} ${-r * .28}l${r * .72} ${-r * .28}Z`
const ray = (deg: number, spread: number) => {
  const at = (a: number) => `${(512 + 640 * Math.cos(a * Math.PI / 180)).toFixed(0)} ${(621 + 640 * Math.sin(a * Math.PI / 180)).toFixed(0)}`
  return `M512 621L${at(deg - spread / 2)}L${at(deg + spread / 2)}Z`
}

export function SkinOverlay({ id }: { id: SkinId }) {
  switch (id) {
    case 'stripes': return <g fill="#fff" opacity=".34" data-skin-overlay={id}>
      {[60, 250, 440, 630].map(x => <path key={x} d={`M${x} 834 L${x + 220} 408 h80 L${x + 80} 834Z`} />)}
    </g>
    case 'split': return <rect x="512" width="512" height="1024" fill="#fff" opacity=".34" data-skin-overlay={id} />
    case 'hoops': return <g fill="#fff" opacity=".34" data-skin-overlay={id}>{[408, 558, 708].map(y => <rect key={y} y={y} width="1024" height="56" />)}</g>
    case 'sunburst': return <g fill="#fff" opacity=".3" data-skin-overlay={id}>{Array.from({ length: 12 }, (_, index) => <path key={index} d={ray(index * 30, 15)} />)}</g>
    case 'dipped': return <rect y="600" width="1024" height="424" fill="var(--dca-part-shadow-color, #0b1020)" opacity=".4" data-skin-overlay={id} />
    case 'pinwheel': return <g fill="#fff" opacity=".3" data-skin-overlay={id}>
      <g className="dca-fx-pinwheel" style={{ transformOrigin: '512px 621px' }}>{[45, 135, 225, 315].map(deg => <path key={deg} d={ray(deg, 45)} />)}</g>
    </g>
    default: return null
  }
}

const sheen = <g className="dca-fx dca-fx-sheen" fill="#fff">
  <path d="M120 834 L420 408 h58 L178 834Z" opacity=".38" />
  <path d="M200 834 L500 408 h22 L222 834Z" opacity=".55" />
</g>
const stars = <g fill="#fff">{[[372, 432], [556, 432], [740, 438], [758, 530], [762, 650], [400, 812], [624, 812], [268, 760]].map(([cx, cy], index) => <path key={`${cx}-${cy}`} className="dca-fx dca-fx-node" d={star4(index % 2 ? 10 : 15)} transform={`translate(${cx} ${cy})`} style={{ animationDelay: `${(index * .43) % 1.8}s` }} />)}</g>
const scanline = <g className="dca-fx dca-fx-scanline" fill="#7dff8a">
  <rect y="250" width="1024" height="110" opacity=".14" />
  <rect y="320" width="1024" height="40" opacity=".6" />
</g>
const effects: Partial<Record<PaintSkinId, ReactNode>> = { chrome: sheen, holographic: sheen, gold: sheen, galaxy: stars, terminal: scanline }

const sweep = (skin: string, stops: ReactNode) => (id: string) => <linearGradient id={id} className={`dca-skin-paint dca-skin-${skin}`} x1="279" y1="766" x2="736" y2="334" gradientUnits="userSpaceOnUse">{stops}</linearGradient>
const gradients: Record<PaintSkinId, (id: string) => ReactNode> = {
  chrome: sweep('chrome', <>
    <stop offset="0" stopColor="#3c4658" /><stop offset=".36" stopColor="#9aa6b8" /><stop offset=".5" stopColor="#f4f7fb" />
    <stop offset=".54" stopColor="#6b7688" /><stop offset=".8" stopColor="#c3ccd8" /><stop offset="1" stopColor="#ffffff" />
  </>),
  holographic: sweep('holo', <>
    <stop offset="0" stopColor="#ff8ad4" /><stop offset=".25" stopColor="#ffd28a" /><stop offset=".5" stopColor="#c9ff8a" />
    <stop offset=".75" stopColor="#8ad8ff" /><stop offset="1" stopColor="#c98aff" />
  </>),
  gold: sweep('gold', <>
    <stop offset="0" stopColor="#b8862c" /><stop offset=".4" stopColor="#f6d365" /><stop offset=".52" stopColor="#fff3c4" />
    <stop offset=".64" stopColor="#d9a53a" /><stop offset="1" stopColor="#ffe9a8" />
  </>),
  lava: sweep('lava', <>
    <stop offset="0" stopColor="#4a1208" /><stop offset=".3" stopColor="#e8451f" /><stop offset=".55" stopColor="#ffb347" />
    <stop offset=".8" stopColor="#e8451f" /><stop offset="1" stopColor="#7a1f0e" />
  </>),
  galaxy: sweep('galaxy', <>
    <stop offset="0" stopColor="#1b1040" /><stop offset=".3" stopColor="#4c1d95" /><stop offset=".55" stopColor="#2563eb" />
    <stop offset=".8" stopColor="#0b3d62" /><stop offset="1" stopColor="#7c3aed" />
  </>),
  bumblebee: id => <linearGradient id={id} className="dca-skin-paint dca-skin-bumblebee" x1="279" y1="766" x2="431" y2="622" gradientUnits="userSpaceOnUse" spreadMethod="repeat">
    <stop offset="0" stopColor="#f6c343" /><stop offset=".5" stopColor="#f6c343" /><stop offset=".5" stopColor="#2b2f3a" /><stop offset="1" stopColor="#2b2f3a" />
  </linearGradient>,
  aurora: sweep('aurora', <>
    <stop offset="0" stopColor="#0a1f33" /><stop offset=".3" stopColor="#1fbf8f" /><stop offset=".55" stopColor="#5eead4" />
    <stop offset=".8" stopColor="#7c6cf0" /><stop offset="1" stopColor="#122046" />
  </>),
  terminal: sweep('terminal', <>
    <stop offset="0" stopColor="#0b0f14" /><stop offset=".55" stopColor="#111a22" /><stop offset="1" stopColor="#1b2a2a" />
  </>),
  patina: sweep('patina', <>
    <stop offset="0" stopColor="#2a8a78" /><stop offset=".38" stopColor="#58b39a" /><stop offset=".5" stopColor="#b8733f" />
    <stop offset=".78" stopColor="#d98e52" /><stop offset="1" stopColor="#f2b27a" />
  </>),
  thermal: sweep('thermal', <>
    <stop offset="0" stopColor="#1a0b5e" /><stop offset=".3" stopColor="#5b1fa8" /><stop offset=".55" stopColor="#c02a6b" />
    <stop offset=".8" stopColor="#ff7a1f" /><stop offset="1" stopColor="#3b1488" />
  </>),
}
export function skinPaint(id: SkinId): BodyPaint | undefined {
  return isPaintSkin(id) ? { render: gradients[id], shadow: paintMaterials[id].shadow } : undefined
}
export function SkinPaintSurface({ id }: { id: SkinId }) {
  if (!isPaintSkin(id)) return null
  return <g data-skin-overlay={id}>
    {effects[id]}
    <rect className="dca-fx dca-skin-energy" {...materialBounds} fill={paintMaterials[id].accent} opacity="0" />
  </g>
}

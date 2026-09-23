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
    case 'quartered': return <path d="M512 0H1024V600H512ZM0 600H512V1024H0Z" fill="#fff" opacity=".34" data-skin-overlay={id} />
    case 'bowtie': return <path d={`${ray(0, 90)}${ray(180, 90)}`} fill="#fff" opacity=".34" data-skin-overlay={id} />
    // Each ring is 1.793x the one inside it, the end scale of dca-fx-sonar, so the loop hands every ring to the next.
    case 'sonar': return <g fill="none" stroke="#fff" opacity=".34" data-skin-overlay={id}>
      <g className="dca-fx-sonar" style={{ transformOrigin: '512px 621px' }}>
        {[[98, 28], [175.5, 51], [314.5, 91]].map(([r, width]) => <circle key={r} cx="512" cy="621" r={r} strokeWidth={width} />)}
      </g>
    </g>
    case 'tide': return <path className="dca-fx-tide" d={`M-17 630q50-100 100 0${'t100 0'.repeat(10)}V1400H-17Z`} fill="var(--dca-part-shadow-color, #0b1020)" opacity=".4" data-skin-overlay={id} />
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
const rainbow = (className: string, opacity?: string) => <g className={`dca-fx dca-fx-sheen ${className}`} opacity={opacity}>
  {['#ff5f6d', '#ffa53a', '#ffe066', '#6ee07a', '#4fb4ff', '#a276ff'].map((fill, k) => <path key={fill} d={`M${60 + 25 * k} 834 L${360 + 25 * k} 408 h25 L${85 + 25 * k} 834Z`} fill={fill} opacity=".75" />)}
</g>
const prism = <>{rainbow('dca-fx-prism')}{rainbow('dca-fx-prism-echo', '0')}</>
const accretion = <g className="dca-fx dca-fx-void" fill="#ffb04a" style={{ transformOrigin: '512px 621px' }}>
  {[[0, .9], [-18, .6], [-36, .35], [-54, .15]].map(([deg, opacity]) => <path key={deg} d={ray(deg, 18)} opacity={opacity} />)}
</g>
const effects: Partial<Record<PaintSkinId, ReactNode>> = { chrome: sheen, holographic: sheen, gold: sheen, candy: sheen, galaxy: stars, terminal: scanline, prism, void: accretion }

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
  candy: sweep('candy', <>
    <stop offset="0" stopColor="#2a0309" /><stop offset=".3" stopColor="#7d0a1c" /><stop offset=".55" stopColor="#c8102e" />
    <stop offset=".8" stopColor="#ff4d63" /><stop offset="1" stopColor="#a30c24" />
  </>),
  racer: id => <linearGradient id={id} className="dca-skin-paint dca-skin-racer" x1="0" y1="408" x2="0" y2="834" gradientUnits="userSpaceOnUse">
    <stop offset="0" stopColor="#7cbbe3" /><stop offset=".45" stopColor="#66a9d8" /><stop offset=".45" stopColor="#f28a1c" />
    <stop offset=".73" stopColor="#f28a1c" /><stop offset=".73" stopColor="#5a9fd0" /><stop offset="1" stopColor="#4f94c6" />
  </linearGradient>,
  prism: sweep('prism', <>
    <stop offset="0" stopColor="#2b2f4f" /><stop offset=".5" stopColor="#4f5580" /><stop offset="1" stopColor="#7b82b0" />
  </>),
  void: id => <radialGradient id={id} className="dca-skin-paint dca-skin-void" cx="512" cy="621" r="345" gradientUnits="userSpaceOnUse">
    <stop offset="0" stopColor="#030206" /><stop offset=".55" stopColor="#06040d" /><stop offset=".72" stopColor="#140a2a" />
    <stop offset=".88" stopColor="#3a1a78" /><stop offset="1" stopColor="#6a3fd0" />
  </radialGradient>,
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

import type { ReactNode } from 'react'
import type { BackId } from './items.js'
import { Detail, Glow, Silhouette as S, useClipId } from './DecorationShapes.js'

const mirrored = (art: ReactNode) => <>{art}<g transform="matrix(-1 0 0 1 1024 0)">{art}</g></>

function Wing({ outer, inner, fill, lining, outline = '#fff', flap = 'dca-fx-flap', children }: { outer: string; inner?: string; fill: string; lining?: string; outline?: string; flap?: string; children?: ReactNode }) {
  return <g className={flap} style={{ transformOrigin: '272px 560px' }}>
    <path d={outer} fill={fill} stroke={outline} strokeWidth="18" strokeLinejoin="round" paintOrder="stroke fill" />
    {inner && <path d={inner} fill={lining} />}
    {children}
  </g>
}
function Wings(props: Parameters<typeof Wing>[0]) {
  return mirrored(<Wing {...props} />)
}
const batWing = {
  outer: 'M272 520C200 470 120 400 60 340c30 80 10 130-20 160 70 20 90 60 60 100 70 10 100 50 90 100h82Z',
  inner: 'M262 540c-60-40-120-96-166-146 20 60 6 100-16 128 56 16 76 50 56 84 56 10 82 44 74 88h52Z',
}
const butterflyWing = {
  outer: 'M272 540C190 380 60 340 20 420c-14 56 50 96 110 120-80 12-120 70-80 130 56 56 160 26 232-40Z',
  inner: 'M262 560c-70-120-170-150-196-90-8 40 44 70 96 90-60 14-90 60-60 100 46 40 130 14 180-44Z',
}
const angelWing = {
  outer: 'M272 520C180 440 80 420 40 470c20 50 70 70 120 80-60 20-90 60-70 100 50 20 110 0 160-40-30 40-30 80 0 100 40-10 80-60 100-120Z',
  inner: 'M262 540C190 480 110 462 78 492c16 36 56 52 96 60-46 16-70 46-56 76 40 14 90-4 130-36-20 30-16 58 8 70 30-10 58-46 72-92Z',
}
const dragonWing = {
  outer: 'M272 500C160 380 60 300 20 340c0 60 60 100 100 130-60 30-100 90-80 150 60 10 130-20 190-70-40 60-40 120 0 150 60-20 100-90 110-160Z',
  inner: 'M262 530C170 430 90 370 60 396c4 44 50 76 84 100-50 24-84 74-70 122 50 4 110-24 160-66-30 48-30 96 0 122 44-18 78-76 86-134Z',
}

function SunRays() {
  const rays = Array.from({ length: 12 }, (_, index) => {
    const a = (index * 30 * Math.PI) / 180, w = (9 * Math.PI) / 180
    const p = (r: number, t: number) => `${(512 + r * Math.cos(t)).toFixed(0)} ${(640 + r * Math.sin(t)).toFixed(0)}`
    return <path key={index} d={`M${p(300, a - w)} L${p(560, a)} L${p(300, a + w)}Z`} fill={index % 2 ? '#ffcf11' : '#f6b500'} stroke="#fff" strokeWidth="14" strokeLinejoin="round" paintOrder="stroke fill" />
  })
  return <g className="dca-fx-rotate" style={{ transformOrigin: '512px 640px' }}>{rays}</g>
}
const balloon = (cx: number, cy: number, fill: string, delay?: string) => <g className="dca-fx-hover" style={{ transformOrigin: `${cx}px ${cy}px`, animationDelay: delay }}>
  <path d={`M${cx} ${cy + 52}L${cx - 8} ${cy + 66}h16Z`} fill={fill} />
  <ellipse cx={cx} cy={cy} rx="46" ry="54" fill={fill} stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
  <ellipse cx={cx - 14} cy={cy - 18} rx="10" ry="16" fill="#fff" opacity=".6" />
</g>
const tether = (d: string) => <><path d={d} stroke="#fff" strokeWidth="16" strokeLinecap="round" /><path d={d} stroke="#8b95a5" strokeWidth="6" strokeLinecap="round" /></>

function SolarPanel() {
  return <>
    <path d="M300 455 188 350" stroke="#fff" strokeWidth="46" strokeLinecap="round" />
    <path d="M300 455 188 350" stroke="#8b95a5" strokeWidth="24" strokeLinecap="round" />
    <g transform="translate(188 350) rotate(-26)">
      <rect x="-68" y="-192" width="136" height="178" rx="14" fill="#2f4a8a" stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
      <path d="M-54-178h50v44h-50Zm58 0h50v44H4Zm-58 53h50v44h-50Zm58 0h50v44H4Zm-58 53h50v44h-50Zm58 0h50v44H4Z" fill="#5b7fd6" />
      <circle r="18" fill="#8b95a5" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
    </g>
  </>
}

const bladePath = 'M-40-22H720L780 0 720 22H-40Z'
function Sword({ blade, glow, guard, children }: { blade: string; glow: string; guard: string; children?: ReactNode }) {
  const clip = useClipId()
  return <>
    <defs><clipPath id={clip}><path d={bladePath} /></clipPath></defs>
    <Glow blur={16} className="dca-fx-pulse"><path d="M-40-30H724L796 0 724 30H-40Z" fill={glow} opacity=".85" /></Glow>
    <circle cx="-166" r="22" fill={guard} stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
    <rect x="-160" y="-16" width="108" height="32" rx="12" fill="#2b2f3a" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
    <S d={bladePath} fill={blade} stroke={16} />
    {children}
    <g clipPath={`url(#${clip})`}><g transform="translate(689 0) scale(.3 1)"><g className="dca-fx dca-fx-sheen" fill="#fff"><path d="M3-30h67l-73 60h-67Z" opacity=".85" /></g></g></g>
    <rect x="-64" y="-62" width="24" height="124" rx="10" fill={guard} stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
    <rect x="-14" y="-36" width="30" height="72" rx="10" fill="#8b95a5" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
  </>
}
const leftShoulder = 'translate(265 392) rotate(43)'

const guitarBody = 'M-86 0C-86-48-48-86 0-86 42-86 66-62 90-54 110-46 124-64 160-64 196-64 224-36 224 0 224 36 196 64 160 64 124 64 110 46 90 54 66 62 42 86 0 86-48 86-86 48-86 0Z'

const orbit = { transform: 'translate(512 760) rotate(-12)', rx: 450, ry: 104 }
const orbitBodies: { key: string; delay?: string; art: ReactNode }[] = [
  { key: 'a', art: <><ellipse rx="64" ry="18" fill="none" stroke="#fff" strokeWidth="22" transform="rotate(-18)" /><ellipse rx="64" ry="18" fill="none" stroke="#ffe08a" strokeWidth="9" transform="rotate(-18)" /><circle r="44" fill="#fff" /><circle r="34" fill="#f6b500" /><path d="M-64 0a64 18 0 0 0 128 0" fill="none" stroke="#ffe08a" strokeWidth="9" transform="rotate(-18)" /></> },
  { key: 'b', delay: '-2.67s', art: <><circle r="34" fill="#fff" /><circle r="25" fill="#c9d2ff" /></> },
  { key: 'c', delay: '-5.33s', art: <><circle r="26" fill="#fff" /><circle r="17" fill="#ff9ad9" /></> },
]
function OrbitBodies({ side }: { side: 'front' | 'back' }) {
  return <>{orbitBodies.map(body => <g key={body.key} className={`dca-fx dca-fx-orbit dca-fx-orbit-${side} dca-fx-orbit-${body.key}`} style={body.delay ? { animationDelay: body.delay } : undefined}>{body.art}</g>)}</>
}
function OrbitArc({ side }: { side: 'front' | 'back' }) {
  const sweep = side === 'back' ? 1 : 0
  const outer = `M${-orbit.rx} 0A${orbit.rx} ${orbit.ry} 0 0 ${sweep} ${orbit.rx} 0`
  return <>
    <path d={outer} fill="none" stroke="#fff" strokeWidth="46" />
    <path d={outer} fill="none" stroke="#b9c4ff" strokeWidth="30" />
  </>
}

const koiOrbit = `${orbit.transform} scale(.88)`
const koi = <>
  <S d="M68 0 134-56 110 0 134 56Z" fill="#e8654f" />
  <S d="M-114 2C-102-42-60-60-16-58S62-28 82 0C62 28 28 56-16 58S-106 44-114 2Z" fill="#ed985f" />
  <ellipse cx="-10" cy="-22" rx="48" ry="24" transform="rotate(8 -10 -22)" fill="#fff4ef" />
</>
function KoiBodies({ side }: { side: 'front' | 'back' }) {
  const art = side === 'front' ? koi : <g transform="scale(-1 1)">{koi}</g>
  return <>{['a', 'c'].map(key => <g key={key} className={`dca-fx-orbit dca-fx-orbit-${side} dca-fx-orbit-${key}`} style={key === 'c' ? { animationDelay: '-4s' } : undefined}>{art}</g>)}</>
}

// The tail spirals inward so its tip ends behind the antenna light or the hat crown.
function cometTail(width: number) {
  const steps = 18, from = -28, to = -100
  const point = (step: number, side: number) => {
    const t = step / steps, a = ((from + (to - from) * t) * Math.PI) / 180, r = 380 - 120 * t + side * (width / 2) * (1 - t) ** .8
    return `${(512 + r * Math.cos(a)).toFixed(0)} ${(520 + r * Math.sin(a)).toFixed(0)}`
  }
  const outer = Array.from({ length: steps + 1 }, (_, step) => point(step, 1))
  const inner = Array.from({ length: steps }, (_, step) => point(steps - 1 - step, -1))
  return `M${[...outer, ...inner].join('L')}Z`
}

const halo = { cx: 512, cy: 229, r: 168 }

const oval = (cx: number, cy: number, rx: number, ry = rx) => `M${cx - rx} ${cy}a${rx} ${ry} 0 1 0 ${2 * rx} 0a${rx} ${ry} 0 1 0 ${-2 * rx} 0Z`

function Floatie({ side }: { side: 'front' | 'back' }) {
  const arc = (dy: number) => `M${-orbit.rx} ${dy}A${orbit.rx} ${orbit.ry} 0 0 ${side === 'back' ? 1 : 0} ${orbit.rx} ${dy}`
  return <g className="dca-fx-hover"><g transform="translate(512 790) rotate(-6)">
    <path d={arc(0)} stroke="#fff" strokeWidth="138" />
    <path d={arc(0)} stroke="#dbab70" strokeWidth="120" />
    <path d={arc(-16)} stroke="#f2a0b4" strokeWidth="84" />
    {side === 'front' && <path d="M384 32l-18 10M252 66l-20 4M76 84l20 4M-110 82l-18 8M-282 62l20 6M-400 24l-14 12" stroke="#fff" strokeWidth="12" strokeLinecap="round" />}
  </g></g>
}

function TeslaCoil() {
  return <>
    <path d="M300 470 212 410" stroke="#fff" strokeWidth="46" strokeLinecap="round" />
    <path d="M300 470 212 410" stroke="#8b95a5" strokeWidth="24" strokeLinecap="round" />
    <rect x="180" y="226" width="64" height="190" rx="14" fill="#c9d2ff" stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
    <path d="M180 270h64M180 306h64M180 342h64M180 378h64" stroke="#8b95a5" strokeWidth="12" />
    <ellipse cx="212" cy="212" rx="84" ry="36" fill="#8b95a5" stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
    <ellipse cx="212" cy="204" rx="46" ry="13" fill="#5b6577" />
  </>
}
const teslaArc = 'M212 186L246 142L296 160L330 104L384 124L420 76L470 96L512 58L556 92L604 72L642 122L694 104L728 156L776 138L812 186'

const dragonTail = 'M313 773 275 795 241 809 211 815 184 815 161 810 140 799 121 782 106 759 94 729 88 693 88 651 94 605 58 595 44 645 38 694 40 739 49 782 66 820 92 854 125 880 165 898 211 905 260 903 313 890 367 867Z'

type Pt = readonly [number, number]
const jade = '#2f9e7a', mint = '#a7e8cf', gold = '#f6b500', ink = '#16302a'
const n0 = (v: number) => Math.round(v)
const cr = (a: number, b: number, c: number, d: number, t: number) => .5 * (2 * b + (c - a) * t + (2 * a - 5 * b + 4 * c - d) * t * t + (3 * b - a - 3 * c + d) * t * t * t)
const dcr = (a: number, b: number, c: number, d: number, t: number) => .5 * ((c - a) + 2 * (2 * a - 5 * b + 4 * c - d) * t + 3 * (3 * b - a - 3 * c + d) * t * t)
function along(spine: Pt[], t: number) {
  const seg = spine.length - 1, u = Math.min(Math.max(t, 0), 1) * seg, k = Math.min(Math.floor(u), seg - 1), f = u - k
  const p = (i: number) => spine[Math.min(Math.max(i, 0), seg)]
  const a = p(k - 1), b = p(k), c = p(k + 1), d = p(k + 2)
  const tx = dcr(a[0], b[0], c[0], d[0], f), ty = dcr(a[1], b[1], c[1], d[1], f), len = Math.hypot(tx, ty) || 1
  return { x: cr(a[0], b[0], c[0], d[0], f), y: cr(a[1], b[1], c[1], d[1], f), tx: tx / len, ty: ty / len }
}
function smooth(points: Pt[]) {
  const mid = (i: number) => { const a = points[(i + points.length) % points.length], b = points[(i + 1) % points.length]; return `${n0((a[0] + b[0]) / 2)} ${n0((a[1] + b[1]) / 2)}` }
  return `M${mid(-1)}${points.map((p, i) => `Q${n0(p[0])} ${n0(p[1])} ${mid(i)}`).join('')}Z`
}
// A tapered band along a Catmull-Rom spine; offset and share place it across the width (positive = the spine's outer normal).
function tube(spine: Pt[], width: (t: number) => number, from = 0, to = 1, offset = 0, share = 1, steps = 36) {
  const left: Pt[] = [], right: Pt[] = []
  for (let i = 0; i <= steps; i++) {
    const t = from + ((to - from) * i) / steps, s = along(spine, t), w = width(t), c = offset * w, h = (share * w) / 2
    left.push([s.x - s.ty * (c + h), s.y + s.tx * (c + h)]); right.push([s.x - s.ty * (c - h), s.y + s.tx * (c - h)])
  }
  return smooth([...left, ...right.reverse()])
}
function ridge(spine: Pt[], width: (t: number) => number, from: number, to: number, count: number, height: number, side = 1, lean = .5) {
  let d = ''
  for (let i = 0; i < count; i++) {
    const t = from + ((to - from) * (i + .5)) / count, s = along(spine, t), w = width(t), e = w * .4 * side, h = height * w / 100, b = h * .6
    const bx = s.x - s.ty * e, by = s.y + s.tx * e, nx = -s.ty * side, ny = s.tx * side
    d += `M${n0(bx - s.tx * b)} ${n0(by - s.ty * b)}L${n0(bx + nx * h + s.tx * lean * h)} ${n0(by + ny * h + s.ty * lean * h)}L${n0(bx + s.tx * b)} ${n0(by + s.ty * b)}Z`
  }
  return d
}
function tuft(x: number, y: number, angle: number, len: number, w: number, curl = 1) {
  const a = (angle * Math.PI) / 180, c = Math.cos(a), s = Math.sin(a)
  const p = (u: number, v: number) => `${n0(x + u * c - v * curl * s)} ${n0(y + u * s + v * curl * c)}`
  return `M${p(0, -w / 2)}C${p(len * .4, -w * .7)} ${p(len * .8, -w * .5)} ${p(len, -w * .1)}C${p(len * .7, w * .05)} ${p(len * .35, w * .45)} ${p(0, w / 2)}Z`
}
// A tapered flame lock from (x, y) toward angle; bend curls the tip clockwise when positive.
function lock(x: number, y: number, angle: number, len: number, w: number, bend = .4) {
  const a = (angle * Math.PI) / 180, c = Math.cos(a), s = Math.sin(a)
  const at = (u: number, v: number): Pt => [x + u * c - v * s, y + u * s + v * c]
  return tube([at(0, 0), at(len * .35, -bend * len * .1), at(len * .7, bend * len * .06), at(len, bend * len * .32)], t => w * (1 - t) + 3, 0, 1, 0, 1, 14)
}
function talon(x: number, y: number, angle: number, len: number, w: number, curl = 1) {
  const a = (angle * Math.PI) / 180, c = Math.cos(a), s = Math.sin(a)
  const p = (u: number, v: number) => `${n0(x + u * c - v * curl * s)} ${n0(y + u * s + v * curl * c)}`
  return `M${p(0, -w / 2)}Q${p(len * .7, -w * .6)} ${p(len, w * .35)}Q${p(len * .45, w * .2)} ${p(0, w / 2)}Z`
}
function Plane({ d, fill, contour = 20 }: { d: string[]; fill: string; contour?: number }) {
  return <>{d.map((p, i) => <path key={`o${i}`} d={p} stroke="#fff" strokeWidth={contour} strokeLinejoin="round" />)}{d.map((p, i) => <path key={i} d={p} fill={fill} />)}</>
}
function Antlers({ d, width = 22 }: { d: string; width?: number }) {
  return <g strokeLinecap="round" strokeLinejoin="round"><path d={d} stroke="#fff" strokeWidth={width + 20} /><path d={d} stroke={gold} strokeWidth={width} /></g>
}
const sea = { deep: '#1e3f8f', band: '#5fb3ec', foam: '#fff8e6' }
function claw(x: number, y: number, deg: number, len: number, w: number, bend = .35) {
  const a = (deg * Math.PI) / 180, c = Math.cos(a), s = Math.sin(a), h = w / 2, b = len * bend
  const p = (u: number, v: number) => `${(x + u * c - v * s).toFixed(0)} ${(y + u * s + v * c).toFixed(0)}`
  return `M${p(-h, 0)}C${p(-h, -h)} ${p(len * .55, -h * 1.1)} ${p(len, b)}C${p(len * .55, b * .45 + h * .5)} ${p(h * .2, h)} ${p(-h, 0)}Z`
}
type Claw = Parameters<typeof claw>
type Drop = readonly [x: number, y: number, r: number]
interface WaterMass { motion: string; pivot: string; body: string; band: string; claws: Claw[]; stagger: number; glint: { className: string; d: string }; spray: Drop[] }
const swing = (points: Pt[]) => points.slice(1).map(([x, y], i) => { const [px, py] = points[i], k = (x - px) * .36; return `C${n0(px + k)} ${py} ${n0(x - k)} ${y} ${x} ${y}` }).join('')
const rim = 12

function Foam({ mass, paint }: { mass: WaterMass; paint: (d: string) => ReactNode }) {
  return <>{mass.claws.map((c, i) => <g key={i} className="dca-fx-reach" style={{ transformOrigin: `${c[0]}px ${c[1]}px`, animationDelay: `${(mass.stagger + i * .45).toFixed(2)}s` }}>{paint(claw(...c))}</g>)}</>
}
function Glint({ clip: shape, className, d }: { clip: string; className: string; d: string }) {
  const clip = useClipId()
  return <><defs><clipPath id={clip}><path d={shape} /></clipPath></defs><g clipPath={`url(#${clip})`}><path className={`dca-fx ${className}`} d={d} fill="#f4fbff" opacity=".92" /></g></>
}
function Spray({ drops }: { drops: Drop[] }) {
  return <g className="dca-fx">{drops.map(([x, y, r], i) => <g key={i} className="dca-fx-spray" style={{ animationDelay: `${(-i * 4.8 / drops.length).toFixed(2)}s` }}>
    <circle cx={x} cy={y} r={r + 9} fill="#fff" /><circle cx={x} cy={y} r={r} fill={sea.band} /><circle cx={x - r * .25} cy={y - r * .25} r={r * .55} fill={sea.foam} />
  </g>)}</g>
}
// All contours are drawn before all fills so masses on different clocks overlap without seams; foam keeps a water-coloured rim off the white outline.
function Water({ masses }: { masses: WaterMass[] }) {
  return <g strokeLinejoin="round">
    {masses.map((m, i) => <g key={`c${i}`} className={m.motion} style={{ transformOrigin: m.pivot }}>
      <path d={m.body} stroke="#fff" strokeWidth="18" />
      <Foam mass={m} paint={d => <path d={d} fill="#fff" stroke="#fff" strokeWidth={2 * rim + 18} />} />
    </g>)}
    {masses.map((m, i) => <g key={`f${i}`} className={m.motion} style={{ transformOrigin: m.pivot }}>
      <path d={m.body} fill={sea.deep} />
      <Foam mass={m} paint={d => <path d={d} fill={sea.deep} stroke={sea.deep} strokeWidth={2 * rim} />} />
      <Detail><path d={m.band} fill={sea.band} /><Glint clip={m.band} {...m.glint} /></Detail>
      <Foam mass={m} paint={d => <><path d={d} fill={sea.band} /><path className="dca-fx-foam" d={d} fill={sea.foam} /></>} />
      <Spray drops={m.spray} />
    </g>)}
  </g>
}
const curl: WaterMass = {
  motion: 'dca-fx-lean', pivot: '170px 900px', stagger: -3,
  body: 'M230 270C300 270 350 296 352 330C354 356 330 366 306 356C270 340 230 330 200 350C170 370 160 420 164 470C170 580 180 680 200 740C222 806 262 846 330 870L330 880L56 880C38 866 30 836 30 796C20 660 28 520 60 420C92 324 156 270 230 270Z',
  band: tube([[330, 904], [200, 872], [122, 790], [94, 660], [98, 520], [130, 420], [184, 358], [240, 336], [284, 340]], t => 56 - 28 * t),
  claws: [[60, 420, -70, 96, 50, .9], [116, 326, -34, 100, 50, .95], [196, 280, 0, 104, 50, 1], [284, 286, 38, 100, 50, .95], [344, 336, 88, 72, 42, .8]],
  glint: { className: 'dca-fx-glint-rise', d: 'M-40 900L400 830V960L-40 1030Z' },
  spray: [[150, 248, 13], [226, 220, 15], [298, 228, 12], [352, 270, 10], [96, 316, 10]],
}
const swell: WaterMass = {
  motion: 'dca-fx-swell', pivot: '512px 900px', stagger: -5.4,
  body: `M30 886C28 856 44 838 64 836C96 834 124 846 170 850${swing([[170, 850], [300, 834], [430, 858], [560, 836], [700, 852]])}C750 846 776 796 812 786C846 776 880 762 910 770C942 780 964 812 964 848C964 886 950 906 924 914${swing([[924, 914], [810, 944], [690, 916], [570, 944], [450, 916], [330, 944], [210, 916], [90, 944]])}C60 944 32 922 30 886Z`,
  band: tube([[900, 862], [820, 906], [700, 894], [580, 912], [460, 894], [340, 912], [240, 900], [180, 866], [134, 806]], t => 16 + 26 * t ** 2),
  claws: [[58, 846, -84, 58, 36, .8], [96, 842, -54, 60, 36, .85], [134, 850, -26, 54, 34, .8], [870, 776, -40, 64, 38, .9], [914, 772, 6, 66, 38, .95], [950, 806, 56, 54, 34, .8]],
  glint: { className: 'dca-fx-glint-flow', d: 'M1000 760H1120L1060 980H940Z' },
  spray: [[944, 730, 11], [984, 774, 9]],
}

const profileBody: Pt[] = [[280, 262], [322, 186], [400, 128], [512, 106], [630, 118], [730, 176], [810, 266], [880, 370], [920, 500], [935, 640], [925, 760], [948, 850], [992, 856], [1000, 790]]
const profileWidth = (t: number) => 108 - 70 * t
const profileHead = 'M50-30C46-80 10-110-36-108C-62-106-84-96-92-78C-96-68-104-62-116-62C-130-62-138-70-146-84C-160-106-196-100-198-70C-200-52-192-40-180-36C-192-30-204-18-196-4L-86 0C-60 30 10 44 40 20C60 4 60-16 50-30Z'
function CloudDragon() {
  return <g className="dca-fx-sway" style={{ transformOrigin: '262px 380px' }}>
    <Plane d={[tube([[925, 740], [905, 830], [872, 896]], t => 58 - 12 * t), oval(862, 906, 36, 28)]} fill={jade} />
    <Plane d={[talon(836, 916, 150, 32, 18), talon(858, 928, 118, 30, 18), talon(884, 926, 80, 28, 18)]} fill={gold} contour={12} />
    <Plane d={[tube(profileBody, profileWidth), ridge(profileBody, profileWidth, .03, .9, 24, 48, -1)]} fill={jade} />
    <path d={tube(profileBody, profileWidth, .03, .93, .24, .34)} fill={mint} />
    <Plane d={[lock(998, 810, -84, 120, 60, -.5), lock(994, 816, -124, 90, 44, .5)]} fill={mint} />
    <Plane d={[tube([[310, 320], [252, 398], [172, 424]], t => 68 - 18 * t), oval(152, 424, 38, 32)]} fill={jade} />
    <Plane d={[talon(124, 402, -150, 40, 22, -1), talon(116, 426, 176, 42, 22, -1), talon(126, 450, 150, 36, 22, -1), talon(156, 396, -100, 30, 18)]} fill={gold} contour={12} />
    <g transform="translate(254 256) rotate(-4)">
      <g transform="translate(-20 -100) scale(.88) translate(20 100)"><Antlers width={26} d="M-30-104C-20-160 30-206 110-226M4-166C-8-190-4-212 10-230M58-204C66-222 82-236 102-244M10-96C34-140 80-168 150-176M90-162C100-184 118-196 140-202" /></g>
      <Plane d={[lock(30, -70, -40, 130, 50), lock(56, -30, -12, 160, 58), lock(56, 14, 14, 170, 58), lock(34, 44, 40, 150, 54), lock(-10, 60, 70, 120, 48), lock(-150, 70, 60, 90, 36, .3)]} fill={mint} />
      <path d="M-86 0-196-4-172 56-70 14Z" fill={ink} />
      <Plane d={['M-76 12C-110 24-150 44-172 56C-184 64-178 80-164 80C-120 80-60 70-16 50Z']} fill={mint} />
      <Plane d={[profileHead, tuft(20, -70, -30, 56, 30)]} fill={jade} />
      <path d="M-188-2l8 26 8-24ZM-162-1l8 16 8-16ZM-138 0l8 16 8-16ZM-114 0l8 14 8-14ZM-162 50l10-22 8 20Z" fill="#fff" />
      <path d={lock(-96, -86, -24, 120, 26, -.5)} fill={mint} />
      <path d="M-92-66Q-72-86-48-74Q-70-58-92-66Z" fill={ink} />
      <path d={tube([[-190, -30], [-236, -10], [-246, 40], [-220, 84], [-170, 100]], t => 22 - 16 * t)} fill={mint} stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
    </g>
  </g>
}

export function BackDecoration({ id }: { id: BackId }) {
  switch (id) {
    case 'cape': return <g className="dca-fx-sway" style={{ transformOrigin: '512px 452px' }}>
      <path d="M262 452C190 600 130 780 104 930c80-26 160-26 240 0 56-20 112-30 168-30s112 10 168 30c80-26 160-26 240 0-26-150-86-330-158-478Z" fill="#b23a48" stroke="#fff" strokeWidth="18" strokeLinejoin="round" paintOrder="stroke fill" />
      <path d="M296 470C236 600 190 760 176 896c56-16 108-22 168-16 56-14 112-22 168-22s112 8 168 22c60-6 112 0 168 16-14-136-60-296-120-426Z" fill="#d65a68" />
    </g>
    case 'jetpack': return <>
      <g className="dca-fx dca-fx-flame" style={{ transformOrigin: '159px 800px' }}>
        <path d="M159 892c-26-30-34-56-26-90h52c8 34 0 60-26 90Z" fill="#ffb347" />
        <path d="M159 866c-12-18-16-34-10-52h20c6 18 2 34-10 52Z" fill="#ffe08a" />
      </g>
      <g className="dca-fx dca-fx-flame" style={{ transformOrigin: '865px 800px', animationDelay: '.35s' }}>
        <path d="M865 892c-26-30-34-56-26-90h52c8 34 0 60-26 90Z" fill="#ffb347" />
        <path d="M865 866c-12-18-16-34-10-52h20c6 18 2 34-10 52Z" fill="#ffe08a" />
      </g>
      <rect x="118" y="486" width="82" height="290" rx="40" fill="#8b95a5" stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
      <rect x="824" y="486" width="82" height="290" rx="40" fill="#8b95a5" stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
      <rect x="126" y="486" width="66" height="44" rx="20" fill="#e8654f" />
      <rect x="832" y="486" width="66" height="44" rx="20" fill="#e8654f" />
      <path d="M134 776h50l10 30h-70Z" fill="#3c4658" stroke="#fff" strokeWidth="12" strokeLinejoin="round" paintOrder="stroke fill" />
      <path d="M840 776h50l10 30h-70Z" fill="#3c4658" stroke="#fff" strokeWidth="12" strokeLinejoin="round" paintOrder="stroke fill" />
    </>
    case 'bat-wings': return <Wings {...batWing} fill="#5b3a8c" lining="#3d2563" />
    case 'butterfly-wings': return <Wings {...butterflyWing} fill="#f2a0b4" lining="#fff3c4" />
    case 'angel-wings': return <>
      <Glow blur={22} className="dca-fx-pulse">{mirrored(<path d={angelWing.outer} fill="#fff7dc" opacity=".6" />)}</Glow>
      <Wings {...angelWing} fill="#fff" lining="#dfe4ff" outline="#b9c4ff" flap="dca-fx-flap-slow" />
    </>
    case 'dragon-wings': return <Wings {...dragonWing} fill="#b23a48" lining="#7f2634">
      <g className="dca-fx dca-fx-pulse" fill="#ffb347"><circle cx="30" cy="350" r="14" /><circle cx="48" cy="618" r="12" /><circle cx="236" cy="704" r="12" /></g>
    </Wings>
    case 'crescent-moon': return <>
      <path d="M330 130a190 190 0 1 0 176 254a150 150 0 0 1-176-254Z" fill="#f6d365" stroke="#fff" strokeWidth="18" strokeLinejoin="round" paintOrder="stroke fill" />
      <path className="dca-fx dca-fx-sparkle-solo" d="M720 150l12 30 30 12-30 12-12 30-12-30-30-12 30-12Z" fill="#fff3c4" stroke="#fff" strokeWidth="10" strokeLinejoin="round" paintOrder="stroke fill" />
    </>
    case 'star-trail': return <g className="dca-fx-sparkle" fill="#f6b500" stroke="#fff" strokeWidth="14" strokeLinejoin="round" paintOrder="stroke fill">
      <path d="M130 330l20 48 48 20-48 20-20 48-20-48-48-20 48-20Z" />
      <path d="M900 250l16 38 38 16-38 16-16 38-16-38-38-16 38-16Z" style={{ animationDelay: '.6s' }} />
      <path d="M920 690l12 30 30 12-30 12-12 30-12-30-30-12 30-12Z" style={{ animationDelay: '1.1s' }} />
    </g>
    case 'balloons': return <>
      {tether('M150 384 262 560M206 306 262 560M112 484 262 560')}
      {balloon(150, 330, '#e8654f')}{balloon(206, 250, '#f6b500', '.5s')}{balloon(112, 430, '#4f7cf6', '1s')}
    </>
    case 'kite': return <>
      {tether('M885 336c-6 96-50 170-116 226')}
      <g transform="rotate(10 908 206)">
        <S d="M908 122 986 206 908 338 830 206Z" fill="#e8654f" />
        <path d="M908 122V206H830ZM908 206H986L908 338Z" fill="#f6b500" />
      </g>
    </>
    case 'solar-panels': return mirrored(<SolarPanel />)
    case 'power-cord': return <>
      <path d="M902 836C972 868 1006 812 984 772 962 734 908 748 920 792 930 826 976 832 996 806" stroke="#fff" strokeWidth="60" strokeLinecap="round" />
      <path d="M902 836C972 868 1006 812 984 772 962 734 908 748 920 792 930 826 976 832 996 806" stroke="#3c4658" strokeWidth="36" strokeLinecap="round" />
      <g transform="translate(808 792) rotate(25.5)">
        <rect x="-14" y="-40" width="104" height="80" rx="18" fill="#3c4658" stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
        <path d="M30-24v48M54-24v48" stroke="#5b6577" strokeWidth="8" strokeLinecap="round" />
        <rect x="86" y="-20" width="24" height="40" rx="8" fill="#3c4658" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
      </g>
    </>
    case 'surfboard': return <g transform="rotate(8 900 540)">
      <S d="M900 176c44 64 62 204 62 384s-16 290-40 330c-10 16-34 16-44 0-24-40-40-150-40-330s18-320 62-384Z" fill="#f3cf62" />
      <rect x="886" y="250" width="28" height="600" rx="14" fill="#e8654f" />
    </g>
    case 'fox-tail': return <g className="dca-fx-sway" style={{ transformOrigin: '780px 830px' }}>
      <S d="M730 790C840 790 878 720 885 650 890 600 905 560 940 552 972 545 990 580 990 640 990 720 968 800 918 848 868 894 780 900 715 866Z" fill="#ed985f" />
      <path d="M715 866C780 900 868 894 918 848 968 800 990 720 990 640 978 716 948 784 898 818 848 852 780 860 732 850Z" fill="#c96c43" />
      <path d="M885 650C890 600 905 560 940 552 972 545 990 580 990 640L972 626 956 652 936 630 916 654 900 632Z" fill="#fff1dc" />
    </g>
    case 'hero-scarf': return <g className="dca-fx-wave" style={{ transformOrigin: '250px 470px' }}>
      <S d="M252 470C220 490 190 520 150 520 120 520 90 540 62 568L106 580 90 624C120 600 150 580 180 574 210 568 232 548 252 534Z" fill="#b94f50" />
      <S d="M252 418C214 400 180 386 140 398 100 410 70 404 22 380L58 430 26 478C76 494 110 478 146 462 186 446 220 470 252 482Z" fill="#e8654f" />
    </g>
    case 'guitar': return <>
      <g transform="translate(92 822) rotate(-35)">
        <rect x="210" y="-19" width="690" height="38" rx="10" fill="#8b5a2b" stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
        <g fill="#f6d365" stroke="#fff" strokeWidth="8" paintOrder="stroke fill"><circle cx="930" cy="-38" r="10" /><circle cx="972" cy="-38" r="10" /><circle cx="930" cy="38" r="10" /><circle cx="972" cy="38" r="10" /></g>
        <S d="M884-32h116a16 16 0 0 1 16 16v32a16 16 0 0 1-16 16H884Z" fill="#8b5a2b" />
        <S d={guitarBody} fill="#e0ad84" />
        <circle cx="100" r="31" fill="#f6d365" /><circle cx="100" r="20" fill="#8b5a2b" />
        <rect x="-40" y="-30" width="16" height="60" rx="7" fill="#8b5a2b" />
      </g>
      <path d="M826 334C840 384 834 424 800 460" stroke="#fff" strokeWidth="44" strokeLinecap="round" />
      <path d="M826 334C840 384 834 424 800 460" stroke="#3c4658" strokeWidth="22" strokeLinecap="round" />
    </>
    case 'twin-blades': return <>
      <g transform={leftShoulder}><Sword blade="#1f2330" glow="#9fb2ff" guard="#3c4658"><path d="M-28-13H718L772-3" stroke="#dfe6f5" strokeWidth="9" strokeLinecap="round" strokeLinejoin="round" /></Sword></g>
      <g transform="matrix(-1 0 0 1 1024 0)"><g transform={leftShoulder}><Sword blade="#8fdccf" glow="#7fe8d8" guard="#a7b1c0"><path d="M-28 0H728" stroke="#e6fbf6" strokeWidth="10" strokeLinecap="round" /></Sword></g></g>
    </>
    case 'comet': return <>
      <S d={cometTail(110)} fill="#b9c4ff" />
      <path d={cometTail(46)} fill="#fff3c4" />
      <Glow blur={24} className="dca-fx-pulse"><circle cx="848" cy="342" r="96" fill="#fff3c4" opacity=".8" /></Glow>
      <S d="M848 282a60 60 0 1 1 0 120 60 60 0 1 1 0-120Z" fill="#f6b500" />
      <circle cx="828" cy="322" r="20" fill="#fff3c4" />
    </>
    case 'sun-rays': return <SunRays />
    case 'halo': return <>
      <Glow blur={24} className="dca-fx-pulse"><circle cx={halo.cx} cy={halo.cy} r={halo.r} fill="none" stroke="#ffe08a" strokeWidth="74" opacity=".65" /></Glow>
      <circle cx={halo.cx} cy={halo.cy} r={halo.r} fill="none" stroke="#fff" strokeWidth="54" />
      <circle cx={halo.cx} cy={halo.cy} r={halo.r} fill="none" stroke="#f6b500" strokeWidth="34" />
    </>
    case 'orbit-ring': return <g transform={orbit.transform}>
      <Glow blur={22} className="dca-fx-pulse"><ellipse rx={orbit.rx} ry={orbit.ry} fill="none" stroke="#b9c4ff" strokeWidth="60" opacity=".55" /></Glow>
      <OrbitArc side="back" />
      <OrbitBodies side="back" />
    </g>
    case 'koi-orbit': return <g transform={koiOrbit}><KoiBodies side="back" /></g>
    case 'peeking-cat': return <g transform="translate(786 376) rotate(24) scale(1.1)">
      <S d="M-100 14C-100-46-58-84 0-84S100-46 100 14 60 96 0 96-100 74-100 14ZM-92-24-86-138-22-76ZM92-24 86-138 22-76Z" fill="#ed985f" />
      <path d="M-78-52-75-112-40-80ZM78-52 75-112 40-80Z" fill="#c96c43" />
      <path d="M-26-80v24M0-84v30M26-80v24" stroke="#c96c43" strokeWidth="12" strokeLinecap="round" />
      <ellipse cx="-38" cy="6" rx="13" ry="18" fill="#3c4658" /><ellipse cx="38" cy="6" rx="13" ry="18" fill="#3c4658" />
      <ellipse cx="0" cy="50" rx="44" ry="26" fill="#fff1dc" /><path d="M-10 32h20l-10 12Z" fill="#e8654f" />
    </g>
    case 'donut-floatie': return <Floatie side="back" />
    case 'ladybug': return <g transform="translate(232 386) rotate(-34) scale(1.05)" strokeLinecap="round">
      <path d="M-18-92c-10-22-26-34-44-38M18-92c10-22 26-34 44-38" stroke="#fff" strokeWidth="30" fill="none" /><path d="M-18-92c-10-22-26-34-44-38M18-92c10-22 26-34 44-38" stroke="#2b2f3a" strokeWidth="12" fill="none" />
      <S d={`${oval(0, -70, 36, 30)}${oval(0, 26, 72, 92)}`} fill="#e8654f" />
      <path d={oval(0, -70, 36, 30)} fill="#2b2f3a" /><path d="M0-44V118" stroke="#2b2f3a" strokeWidth="10" />
      <g fill="#2b2f3a"><circle cx="-30" cy="-6" r="13" /><circle cx="30" cy="4" r="13" /><circle cx="-24" cy="56" r="13" /><circle cx="28" cy="66" r="12" /><circle cx="-36" cy="-42" r="9" /><circle cx="38" cy="-36" r="9" /></g>
      <circle cx="-12" cy="-76" r="6" fill="#fff" /><circle cx="12" cy="-76" r="6" fill="#fff" />
    </g>
    case 'dragon-tail': return <g className="dca-fx-sway" style={{ transformOrigin: '250px 830px' }}>
      <S d={`M172 892 117 931 118 871ZM106 857 42 868 68 817ZM63 797 2 783 45 748ZM47 721-5 689 45 670Z${dragonTail}`} fill="#7f2634" />
      <g transform="translate(76 600) rotate(16) scale(1.1)"><S d="M0-96C20-70 52-44 50-12 48 8 26 16 10 4L0 20-10 4C-26 16-48 8-50-12-52-44-20-70 0-96Z" fill="#b23a48" /></g>
      <path d={dragonTail} fill="#b23a48" />
    </g>
    case 'drone-buddy': return <>
      {tether('M170 256C176 360 226 424 262 500')}
      <g className="dca-fx-hover"><g transform="translate(170 236) scale(1.4)" strokeLinecap="round">
        {[[-50, -20, -64, 45, 10], [50, -20, -64, 45, 10], [-86, -2, -36, 54, 12], [86, -2, -36, 54, 12]].map(([x, base, top, rx, ry]) => <g key={x}>
          <path d={`M${x} ${base}V${top}`} stroke="#fff" strokeWidth="32" /><path d={`M${x} ${base}V${top}`} stroke="#3c4658" strokeWidth="16" />
          <g transform={`translate(${x} ${top})`}><g className="dca-fx-spin-flat" style={{ transformOrigin: '0px 0px' }}>
            <ellipse rx={rx} ry={ry} fill="#8b95a5" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
          </g></g>
        </g>)}
        <path d="M-86 0-30 12M86 0 30 12" stroke="#fff" strokeWidth="36" /><path d="M-86 0-30 12M86 0 30 12" stroke="#3c4658" strokeWidth="18" />
        <S d={oval(0, 8, 60, 42)} fill="#f4f6fb" />
        <rect x="-54" y="2" width="108" height="22" rx="11" fill="#3c4658" />
        <circle cy="13" r="25" fill="#3c4658" stroke="#fff" strokeWidth="9" paintOrder="stroke fill" />
        <circle cy="13" r="13" fill="#4de3ff" /><circle cx="-4" cy="8" r="4.5" fill="#fff" />
      </g></g>
    </>
    case 'shade-tree': return <>
      <S d="M380 380C404 330 426 290 440 240h144c14 50 36 90 60 140Z" fill="#805840" />
      <g className="dca-fx-sway" style={{ transformOrigin: '512px 320px' }}>
        <S d={[[512, 180, 142], [352, 224, 104], [672, 224, 104], [428, 122, 84], [596, 122, 84]].map(([x, y, r]) => oval(x, y, r)).join('')} fill="#3f8f4f" />
        <path d={[[500, 160, 114], [342, 206, 76], [660, 206, 76], [418, 108, 62], [586, 108, 62]].map(([x, y, r]) => oval(x, y, r)).join('')} fill="#6fbf73" />
      </g>
    </>
    case 'tesla-coils': return <>
      {mirrored(<TeslaCoil />)}
      <Glow blur={14} className="dca-fx-node"><path d={teslaArc} stroke="#4de3ff" strokeWidth="44" strokeLinejoin="round" /></Glow>
      <g className="dca-fx-node" strokeLinejoin="round" strokeLinecap="round">
        <path d={teslaArc} stroke="#fff" strokeWidth="34" />
        <path d={teslaArc} stroke="#4de3ff" strokeWidth="20" />
        <path d={teslaArc} stroke="#fff" strokeWidth="6" />
      </g>
    </>
    case 'great-wave': return <Water masses={[curl, swell]} />
    case 'cloud-dragon': return <CloudDragon />
    default: return null
  }
}

export function BackFrontDecoration({ id }: { id: BackId }) {
  switch (id) {
    case 'orbit-ring': return <g data-back-front={id} transform={orbit.transform}>
      <OrbitArc side="front" />
      <OrbitBodies side="front" />
    </g>
    case 'koi-orbit': return <g data-back-front={id} transform={koiOrbit}><KoiBodies side="front" /></g>
    case 'donut-floatie': return <g data-back-front={id}><Floatie side="front" /></g>
    default: return null
  }
}
export function hasFrontPart(id: BackId): boolean { return id === 'orbit-ring' || id === 'koi-orbit' || id === 'donut-floatie' }

import type { ReactNode } from 'react'
import type { BackId } from './items.js'
import { Glow } from './DecorationShapes.js'

function Wing({ outer, inner, fill, lining, outline = '#fff', flap = 'dca-fx-flap', children }: { outer: string; inner?: string; fill: string; lining?: string; outline?: string; flap?: string; children?: ReactNode }) {
  return <g className={flap} style={{ transformOrigin: '272px 560px' }}>
    <path d={outer} fill={fill} stroke={outline} strokeWidth="18" strokeLinejoin="round" paintOrder="stroke fill" />
    {inner && <path d={inner} fill={lining} />}
    {children}
  </g>
}
function Wings(props: Parameters<typeof Wing>[0]) {
  return <><Wing {...props} /><g transform="matrix(-1 0 0 1 1024 0)"><Wing {...props} /></g></>
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

const halo = { cx: 512, cy: 229, r: 168 }

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
      <Glow blur={22} className="dca-fx-pulse"><path d={angelWing.outer} fill="#fff7dc" opacity=".6" /><path d={angelWing.outer} fill="#fff7dc" opacity=".6" transform="matrix(-1 0 0 1 1024 0)" /></Glow>
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
      <path d="M150 384 262 560M206 306 262 560M112 484 262 560" stroke="#fff" strokeWidth="16" strokeLinecap="round" /><path d="M150 384 262 560M206 306 262 560M112 484 262 560" stroke="#8b95a5" strokeWidth="6" strokeLinecap="round" />
      {balloon(150, 330, '#e8654f')}{balloon(206, 250, '#f6b500', '.5s')}{balloon(112, 430, '#4f7cf6', '1s')}
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
    default: return null
  }
}

export function BackFrontDecoration({ id }: { id: BackId }) {
  switch (id) {
    case 'orbit-ring': return <g data-back-front={id} transform={orbit.transform}>
      <OrbitArc side="front" />
      <OrbitBodies side="front" />
    </g>
    default: return null
  }
}
export function hasFrontPart(id: BackId): boolean { return id === 'orbit-ring' }

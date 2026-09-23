import type { ReactNode } from 'react'
import type { BackId } from './items.js'
import { Glow, Silhouette as S, useClipId } from './DecorationShapes.js'

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
    default: return null
  }
}
export function hasFrontPart(id: BackId): boolean { return id === 'orbit-ring' || id === 'koi-orbit' }

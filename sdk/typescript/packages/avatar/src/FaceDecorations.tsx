import { useId, type ReactNode } from 'react'
import type { FaceId, Zone } from './items.js'
import { itemOf } from './items.js'
import type { Faceplate } from './MascotRig.js'
import { Silhouette as S, useClipId } from './DecorationShapes.js'

const heartLens = 'c-31-11-52-22-52-34 0-10 15-16 29-16 10 0 19 3 23 8 4-5 13-8 23-8 14 0 29 6 29 16 0 12-21 23-52 34Z'
const snorkelTube = 'M792 458V372q0-30 28-40'

export function FaceDecoration({ id }: { id: FaceId }) {
  switch (id) {
    case 'forehead-goggles': return <g strokeLinejoin="round">
      <path d="M374 419h109v46H374Zm167 0h109v46H541Z" fill="#a2c5d1" stroke="#8b7568" strokeWidth="14" />
      <path d="M483 442q29 20 58 0" stroke="#8b7568" strokeWidth="11" fill="none" />
      <path d="m389 429 18 21m149-21 18 21" stroke="#e0eff1" strokeWidth="8" />
    </g>
    case 'shades': return <>
      <rect x="372" y="416" width="116" height="44" rx="16" fill="#2b2f3a" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
      <rect x="536" y="416" width="116" height="44" rx="16" fill="#2b2f3a" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
      <path d="M488 432h48" stroke="#2b2f3a" strokeWidth="12" strokeLinecap="round" />
      <path d="M386 428h44M550 428h44" stroke="#6b7280" strokeWidth="10" strokeLinecap="round" />
    </>
    case 'heart-glasses': return <>
      <S d={`M430 462${heartLens}M594 462${heartLens}`} fill="#c9577a" stroke={12} />
      <path d="M482 426h60" stroke="#c9577a" strokeWidth="12" strokeLinecap="round" />
      <path d="M396 430q2-9 12-11M560 430q2-9 12-11" stroke="#f2a0b4" strokeWidth="9" strokeLinecap="round" fill="none" />
    </>
    case 'headlamp': return <>
      <path d="M300 436h424" stroke="#3c4658" strokeWidth="20" strokeLinecap="round" />
      <S d="M448 416h128a16 16 0 0 1 16 16v8a16 16 0 0 1-16 16H448a16 16 0 0 1-16-16v-8a16 16 0 0 1 16-16ZM484 436a28 28 0 1 1 56 0a28 28 0 1 1-56 0Z" fill="#3c4658" stroke={12} />
      <circle cx="512" cy="436" r="24" fill="#ffd970" stroke="#8b95a5" strokeWidth="6" /><circle cx="512" cy="436" r="8" fill="#fff" />
    </>
    case 'snorkel-mask': return <>
      <path d="M300 436h424" stroke="#3c4658" strokeWidth="14" strokeLinecap="round" />
      <rect x="378" y="411" width="268" height="52" rx="22" fill="#3c4658" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
      <rect x="390" y="422" width="244" height="30" rx="12" fill="#a2c5d1" />
      <path d="m404 428 14 18m190-18 14 18" stroke="#e0eff1" strokeWidth="8" strokeLinecap="round" />
      <g className="dca-part-temple" fill="none" strokeLinecap="round">
        <path d="M724 436h52" stroke="#3c4658" strokeWidth="14" />
        <path d={snorkelTube} stroke="#fff" strokeWidth="52" />
        <path d={snorkelTube} stroke="#ed985f" strokeWidth="36" />
      </g>
    </>
    case 'bow-tie': return <>
      <S d="M512 816 602 788q18-5 18 13v58q0 18-18 13L512 844 422 872q-18 5-18-13v-58q0-18 18-13ZM490 830a22 22 0 1 1 44 0a22 22 0 1 1-44 0Z" fill="#e8654f" stroke={14} />
      <circle cx="512" cy="830" r="22" fill="#b94f50" />
    </>
    case 'neckerchief': return <>
      <S d="M391 786h242a13 13 0 0 1 0 26h-11L524 862q-12 10-24 0L402 812h-11a13 13 0 0 1 0-26Z" fill="#dbaa40" stroke={14} />
      <rect x="378" y="786" width="268" height="26" rx="13" fill="#f3cf62" />
    </>
    case 'bell-collar': return <>
      <rect x="356" y="794" width="312" height="30" rx="15" fill="#e8654f" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <S d="M468 834a44 44 0 1 1 88 0a44 44 0 1 1-88 0Z" fill="#f6b500" stroke={14} />
      <circle cx="512" cy="848" r="11" fill="#805840" /><path d="M512 848v22" stroke="#805840" strokeWidth="10" strokeLinecap="round" />
    </>
    case 'ear-pencil': return <g className="dca-part-temple" transform="rotate(-40 800 420)">
      <S d="M712 420 752 398h122a14 14 0 0 1 14 14v16a14 14 0 0 1-14 14H752Z" fill="#f3cf62" stroke={14} />
      <path d="M712 420 752 398v44Z" fill="#e0ad84" />
      <path d="M860 398h14a14 14 0 0 1 14 14v16a14 14 0 0 1-14 14h-14Z" fill="#f2a0b4" />
    </g>
    default: return null
  }
}

export function isFaceplate(id: FaceId): boolean { return (itemOf(id).zones as readonly Zone[]).includes('screen') }

function useGlowFilter() {
  const id = `dca-mask-glow-${useId().replace(/:/g, '')}`
  return { id, defs: <defs><filter id={id} x="-30%" y="-60%" width="160%" height="220%"><feGaussianBlur stdDeviation="14" /></filter></defs> }
}
function useScanSweep(eyes: string, x: number, y: number, height: number) {
  const clip = useClipId()
  return {
    defs: <defs><clipPath id={clip}><path d={eyes} /></clipPath></defs>,
    sweep: <g className="dca-part-face dca-part-face-operator" data-mask-face="operator-scan" clipPath={`url(#${clip})`}>
      <g className="dca-fx dca-fx-scan-eyes"><rect x={x} y={y} width="10" height={height} fill="#fff" opacity=".95" /><rect x={x - 8} y={y} width="26" height={height} fill="#fff" opacity=".35" /></g>
    </g>,
  }
}
function Light({ name, filter, glow, core, glowOpacity = .85, coreOpacity = 1, effect = 'dca-fx-pulse', children }: {
  name: string; filter: string; glow: string; core: string; glowOpacity?: number; coreOpacity?: number; effect?: string; children: ReactNode
}) {
  return <g className={`dca-part-face dca-part-face-${name}`} data-mask-face={name}>
    <g className={`dca-fx ${effect}`} filter={`url(#${filter})`} fill={glow} stroke={glow} opacity={glowOpacity}>{children}</g>
    <g className="dca-part-eyes" fill={core} stroke={core} opacity={coreOpacity}>{children}</g>
  </g>
}

const goldSlits = 'M364 592 458 578 462 630 368 646ZM660 592 566 578 562 630 656 646Z'
function GoldEyes() {
  const glow = useGlowFilter()
  const scan = useScanSweep(goldSlits, 360, 570, 84)
  return <>
    {glow.defs}
    {scan.defs}
    <path d={goldSlits} fill="#2b2010" />
    <Light name="neutral" filter={glow.id} glow="#6fdcff" core="#dff7ff" coreOpacity={.92}><path d={goldSlits} /><path d="M376 612h76M572 612h76" fill="none" stroke="#fff" strokeWidth="8" strokeLinecap="round" /></Light>
    <Light name="happy" filter={glow.id} glow="#ffd970" core="#fff6d6" glowOpacity={1} effect="dca-fx-flare"><path d={goldSlits} /><path d="M372 612h84M568 612h84" fill="none" stroke="#fff" strokeWidth="14" strokeLinecap="round" /></Light>
    <Light name="operator" filter={glow.id} glow="#ffb547" core="#ffd9a0" glowOpacity={.7} effect="dca-fx-none"><path d={goldSlits} /></Light>
    {scan.sweep}
    <Light name="sleep" filter={glow.id} glow="#6fdcff" core="#6fdcff" glowOpacity={.3} coreOpacity={.28} effect="dca-fx-breathe"><path d={goldSlits} /></Light>
  </>
}

function NeonEyes() {
  const glow = useGlowFilter()
  return <>
    {glow.defs}
    <Light name="neutral" filter={glow.id} glow="#4de3ff" core="#dffaff"><rect x="398" y="578" width="64" height="80" rx="24" /><rect x="562" y="578" width="64" height="80" rx="24" /></Light>
    <Light name="happy" filter={glow.id} glow="#4de3ff" core="#eefdff" glowOpacity={1} effect="dca-fx-flare">
      <path d="M398 602a24 24 0 0 1 24-24h16a24 24 0 0 1 24 24v30q-32-22-64 0Z" /><path d="M562 602a24 24 0 0 1 24-24h16a24 24 0 0 1 24 24v30q-32-22-64 0Z" />
    </Light>
    <Light name="operator" filter={glow.id} glow="#4de3ff" core="#dffaff" glowOpacity={.75} effect="dca-fx-none"><rect x="406" y="606" width="64" height="52" rx="12" /><rect x="554" y="606" width="64" height="52" rx="12" /></Light>
    <Light name="sleep" filter={glow.id} glow="#4de3ff" core="#4de3ff" glowOpacity={.3} coreOpacity={.35} effect="dca-fx-breathe"><rect x="398" y="652" width="64" height="10" rx="5" /><rect x="562" y="652" width="64" height="10" rx="5" /></Light>
  </>
}

const dragonEyes = 'M362 586Q414 548 466 628Q406 640 362 586ZM662 586Q610 548 558 628Q618 640 662 586Z'
const dragonIris = `${dragonEyes}M414 580q16 20 0 40q-16-20 0-40ZM610 580q16 20 0 40q-16-20 0-40Z`
function DragonEyes() {
  const glow = useGlowFilter()
  const scan = useScanSweep(dragonEyes, 356, 560, 80)
  const iris = <path d={dragonIris} fillRule="evenodd" />
  return <>
    {glow.defs}
    {scan.defs}
    <path d={dragonEyes} fill="#140d09" />
    <Light name="neutral" filter={glow.id} glow="#ffb547" core="#ffc766" effect="dca-fx-none">{iris}</Light>
    <Light name="happy" filter={glow.id} glow="#ffd970" core="#fff0b3" glowOpacity={1} effect="dca-fx-flare">{iris}</Light>
    <Light name="operator" filter={glow.id} glow="#ff5a1f" core="#ff8a3d" glowOpacity={.75} effect="dca-fx-none">{iris}</Light>
    {scan.sweep}
    <Light name="sleep" filter={glow.id} glow="#ff6a2a" core="#ff6a2a" glowOpacity={.3} coreOpacity={.28} effect="dca-fx-breathe">{iris}</Light>
  </>
}

const knightEyes = 'M384 602h80v30h-80ZM560 602h80v30h-80Z'
function KnightEyes() {
  const glow = useGlowFilter()
  const scan = useScanSweep(knightEyes, 376, 598, 38)
  return <>
    {glow.defs}
    {scan.defs}
    <Light name="neutral" filter={glow.id} glow="#ffb547" core="#ffd9a0"><path d={knightEyes} /><path d="M394 617h60M570 617h60" fill="none" stroke="#fff6d6" strokeWidth="8" strokeLinecap="round" /></Light>
    <Light name="happy" filter={glow.id} glow="#ffd970" core="#fff6d6" glowOpacity={1} effect="dca-fx-flare"><path d={knightEyes} /><path d="M390 617h68M566 617h68" fill="none" stroke="#fff" strokeWidth="12" strokeLinecap="round" /></Light>
    <Light name="operator" filter={glow.id} glow="#ff6a2a" core="#ff8a3d" glowOpacity={.7} effect="dca-fx-none"><path d={knightEyes} /></Light>
    {scan.sweep}
    <Light name="sleep" filter={glow.id} glow="#ff6a2a" core="#ff6a2a" glowOpacity={.3} coreOpacity={.28} effect="dca-fx-breathe"><path d={knightEyes} /></Light>
  </>
}

const segmentEnds: Record<string, readonly [number, number, number, number]> = {
  a: [0, 0, 1, 0], b: [1, 0, 1, 1], c: [1, 1, 1, 2], d: [0, 2, 1, 2], e: [0, 1, 0, 2], f: [0, 0, 0, 1], g: [0, 1, 1, 1],
}
function segments(lit: string) {
  return [398, 566].flatMap(x => [...lit].map(key => {
    const [c0, r0, c1, r1] = segmentEnds[key]
    const [x0, y0, x1, y1] = [x + c0 * 60, 556 + r0 * 60, x + c1 * 60, 556 + r1 * 60]
    return y0 === y1 ? `M${x0 + 3} ${y0}l11-11H${x1 - 14}l11 11-11 11H${x0 + 14}Z` : `M${x0} ${y0 + 3}l11 11V${y1 - 14}l-11 11-11-11V${y0 + 14}Z`
  })).join('')
}
const colon = 'M505 579h14v14h-14ZM505 639h14v14h-14Z'
function SegmentEyes() {
  const layer = (name: string, lit: string, extra?: ReactNode, opacity?: number) => <g className={`dca-part-face dca-part-face-${name}`} data-mask-face={name} fill="#2f3a2b">
    <path className="dca-part-eyes" d={segments(lit)} opacity={opacity} />{extra}
  </g>
  return <>
    <path d={segments('abcdefg') + colon} fill="#2f3a2b" opacity=".1" />
    {layer('neutral', 'abcdef')}
    {layer('happy', 'abf')}
    {layer('operator', 'g', <path className="dca-fx dca-fx-node" d={colon} />)}
    {layer('sleep', 'd', <path className="dca-fx dca-fx-breathe" d={segments('d')} opacity=".35" />, .35)}
  </>
}

function PortholeGlass() {
  const clip = useClipId()
  return <>
    <defs><clipPath id={clip}><circle cx="512" cy="622" r="104" /></clipPath></defs>
    <circle cx="512" cy="622" r="128" fill="#e3a06b" />
    <circle cx="512" cy="622" r="104" fill="#1f4a5a" />
    <g clipPath={`url(#${clip})`} fill="#c7f6ff">
      <circle className="dca-fx dca-fx-steam" cx="566" cy="556" r="12" />
      <circle className="dca-fx dca-fx-steam" cx="456" cy="688" r="9" style={{ animationDelay: '-.9s' }} />
      <circle className="dca-fx dca-fx-steam" cx="584" cy="690" r="14" style={{ animationDelay: '-1.7s' }} />
    </g>
  </>
}

function Plate({ fill, children }: { fill: string; children?: ReactNode }) {
  const clip = useClipId()
  return <>
    <defs><clipPath id={clip}><rect x="307" y="476" width="410" height="291" rx="68" /></clipPath></defs>
    <rect x="307" y="476" width="410" height="291" rx="68" fill={fill} />
    {children && <g clipPath={`url(#${clip})`}>{children}</g>}
  </>
}
const topLight = (fill: string, opacity: number) => <path d="M307 544v-68h410v68q-205-44-410 0Z" fill={fill} opacity={opacity} />

const mechaEyes = 'M356 604 448 614 452 648 362 636ZM668 604 576 614 572 648 662 636Z'
function MechaEyes() {
  const glow = useGlowFilter()
  const scan = useScanSweep(mechaEyes, 352, 596, 60)
  return <>
    {glow.defs}
    {scan.defs}
    <Light name="neutral" filter={glow.id} glow="#d9ff5a" core="#f6ffb0"><path d={mechaEyes} /></Light>
    <Light name="happy" filter={glow.id} glow="#f6ffb0" core="#ffffff" glowOpacity={1} effect="dca-fx-flare"><path d={mechaEyes} /></Light>
    <Light name="operator" filter={glow.id} glow="#ffb547" core="#ffd9a0" glowOpacity={.75} effect="dca-fx-none"><path d={mechaEyes} /></Light>
    {scan.sweep}
    <Light name="sleep" filter={glow.id} glow="#d9ff5a" core="#d9ff5a" glowOpacity={.25} coreOpacity={.3} effect="dca-fx-breathe"><path d={mechaEyes} /></Light>
  </>
}

function PixelEyes() {
  const glow = useGlowFilter()
  const block = (x: number, y: number, w = 48, h = 48) => <rect key={`${x}-${y}`} x={x} y={y} width={w} height={h} />
  return <>
    {glow.defs}
    <Light name="neutral" filter={glow.id} glow="#7dff8a" core="#c8ffcf">{block(416, 596)}{block(560, 596)}</Light>
    <Light name="happy" filter={glow.id} glow="#7dff8a" core="#c8ffcf" glowOpacity={1} effect="dca-fx-flare">{block(416, 580)}{block(560, 580)}{block(440, 664, 24, 24)}{block(560, 664, 24, 24)}{block(464, 688, 96, 24)}</Light>
    <Light name="operator" filter={glow.id} glow="#7dff8a" core="#c8ffcf" glowOpacity={.75} effect="dca-fx-none">{block(404, 604, 44, 32)}{block(456, 604, 44, 32)}{block(508, 604, 44, 32)}<rect className="dca-fx dca-fx-node" x="560" y="604" width="44" height="32" /></Light>
    <Light name="sleep" filter={glow.id} glow="#7dff8a" core="#7dff8a" glowOpacity={.25} coreOpacity={.45} effect="dca-fx-breathe">{block(416, 640, 48, 12)}{block(560, 640, 48, 12)}</Light>
  </>
}

export function FaceplatePlate({ id }: { id: FaceId }) {
  switch (id) {
    case 'mecha-faceplate': return <g data-faceplate={id}><Plate fill="#f4f6fb">
      <rect x="330" y="582" width="364" height="84" rx="14" fill="#1d2433" />
      <path d="M470 700h84l12 67H458Z" fill="#d94a3a" />
      <rect x="432" y="690" width="16" height="50" rx="4" fill="#1d2433" /><rect x="576" y="690" width="16" height="50" rx="4" fill="#1d2433" />
    </Plate></g>
    case 'pixel-screen': return <g data-faceplate={id}><Plate fill="#1f2a3a">{topLight('#33445a', .8)}</Plate></g>
    case 'gold-faceplate': return <g data-faceplate={id}><Plate fill="#efc04a">
      {topLight('#ffd970', .9)}
      <path d="M307 700v67h410v-67q-205 40-410 0Z" fill="#d9a53a" opacity=".85" />
    </Plate></g>
    case 'neon-visor': return <g data-faceplate={id}><Plate fill="#1d2433">
      <path d="M307 476h410v70q-205-34-410 0Z" fill="#3a4358" opacity=".55" />
      <g className="dca-fx dca-fx-scan">
        <rect x="330" y="476" width="46" height="291" fill="#4de3ff" opacity=".16" />
        <rect x="348" y="476" width="10" height="291" fill="#4de3ff" opacity=".45" />
      </g>
    </Plate></g>
    case 'segment-display': return <g data-faceplate={id}><Plate fill="#b7c49a">{topLight('#c8d4ad', .8)}</Plate></g>
    case 'knight-visor': return <g data-faceplate={id}><Plate fill="#a7b1c0">
      {topLight('#c3cad6', .8)}
      <path d="M344 604q0-8 8-8h320q8 0 8 8v26q0 8-8 8H530v92q0 8-8 8h-20q-8 0-8-8v-92H352q-8 0-8-8Z" fill="#1d2433" />
    </Plate></g>
    case 'porthole-helmet': return <g data-faceplate={id}><Plate fill="#c47a4a">{topLight('#d9925f', .8)}<PortholeGlass /></Plate></g>
    case 'dragon-visor': return <g data-faceplate={id}><Plate fill="#1f2b2c">
      <path d="M307 476h410v58q-41 34-82 0-41 34-82 0-41 34-82 0-41 34-82 0-41 34-82 0Z" fill="#2f4543" />
      <path d="M478 660h68l40 107H438Z" fill="#2f4543" />
      <path d="M476 718q14-14 32-6-10 16-32 6ZM548 718q-14-14-32-6 10 16 32 6Z" fill="#0f1616" />
    </Plate></g>
    default: return null
  }
}

export function FaceplateEyes({ id }: { id: FaceId }) {
  switch (id) {
    case 'mecha-faceplate': return <MechaEyes />
    case 'pixel-screen': return <PixelEyes />
    case 'gold-faceplate': return <GoldEyes />
    case 'neon-visor': return <NeonEyes />
    case 'segment-display': return <SegmentEyes />
    case 'knight-visor': return <KnightEyes />
    case 'porthole-helmet': return <g transform="translate(512 622) scale(.75) translate(-512 -618)"><NeonEyes /></g>
    case 'dragon-visor': return <DragonEyes />
    default: return null
  }
}

export function faceplateOf(id: FaceId | 'none'): Faceplate | undefined {
  if (id === 'none' || !isFaceplate(id)) return undefined
  return { plate: <FaceplatePlate id={id} />, eyes: <FaceplateEyes id={id} /> }
}

import { useId, type ReactNode } from 'react'
import type { FaceId, Zone } from './items.js'
import { itemOf } from './items.js'
import type { Faceplate } from './MascotRig.js'
import { useClipId } from './DecorationShapes.js'

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
    default: return null
  }
}

export function isFaceplate(id: FaceId): boolean { return (itemOf(id).zones as readonly Zone[]).includes('screen') }

function useGlowFilter() {
  const id = `dca-mask-glow-${useId().replace(/:/g, '')}`
  return { id, defs: <defs><filter id={id} x="-30%" y="-60%" width="160%" height="220%"><feGaussianBlur stdDeviation="14" /></filter></defs> }
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
  const clip = useClipId()
  return <>
    {glow.defs}
    <defs><clipPath id={clip}><path d={goldSlits} /></clipPath></defs>
    <path d={goldSlits} fill="#2b2010" />
    <Light name="neutral" filter={glow.id} glow="#6fdcff" core="#dff7ff" coreOpacity={.92}><path d={goldSlits} /><path d="M376 612h76M572 612h76" fill="none" stroke="#fff" strokeWidth="8" strokeLinecap="round" /></Light>
    <Light name="happy" filter={glow.id} glow="#ffd970" core="#fff6d6" glowOpacity={1} effect="dca-fx-flare"><path d={goldSlits} /><path d="M372 612h84M568 612h84" fill="none" stroke="#fff" strokeWidth="14" strokeLinecap="round" /></Light>
    <Light name="operator" filter={glow.id} glow="#ffb547" core="#ffd9a0" glowOpacity={.7} effect="dca-fx-none"><path d={goldSlits} /></Light>
    <g className="dca-part-face dca-part-face-operator" data-mask-face="operator-scan" clipPath={`url(#${clip})`}>
      <g className="dca-fx dca-fx-scan-eyes"><rect x="360" y="570" width="10" height="84" fill="#fff" opacity=".95" /><rect x="352" y="570" width="26" height="84" fill="#fff" opacity=".35" /></g>
    </g>
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
  const clip = useClipId()
  return <>
    {glow.defs}
    <defs><clipPath id={clip}><path d={mechaEyes} /></clipPath></defs>
    <Light name="neutral" filter={glow.id} glow="#d9ff5a" core="#f6ffb0"><path d={mechaEyes} /></Light>
    <Light name="happy" filter={glow.id} glow="#f6ffb0" core="#ffffff" glowOpacity={1} effect="dca-fx-flare"><path d={mechaEyes} /></Light>
    <Light name="operator" filter={glow.id} glow="#ffb547" core="#ffd9a0" glowOpacity={.75} effect="dca-fx-none"><path d={mechaEyes} /></Light>
    <g className="dca-part-face dca-part-face-operator" data-mask-face="operator-scan" clipPath={`url(#${clip})`}>
      <g className="dca-fx dca-fx-scan-eyes"><rect x="352" y="596" width="10" height="60" fill="#fff" opacity=".95" /><rect x="344" y="596" width="26" height="60" fill="#fff" opacity=".35" /></g>
    </g>
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
    default: return null
  }
}

export function FaceplateEyes({ id }: { id: FaceId }) {
  switch (id) {
    case 'mecha-faceplate': return <MechaEyes />
    case 'pixel-screen': return <PixelEyes />
    case 'gold-faceplate': return <GoldEyes />
    case 'neon-visor': return <NeonEyes />
    default: return null
  }
}

export function faceplateOf(id: FaceId | 'none'): Faceplate | undefined {
  if (id === 'none' || !isFaceplate(id)) return undefined
  return { plate: <FaceplatePlate id={id} />, eyes: <FaceplateEyes id={id} /> }
}

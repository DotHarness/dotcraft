import { useId, type ReactNode } from 'react'
import { Glow, type AvatarComponentPack, type AvatarOutfitWear } from '@dotcraft/avatar/react'

const CYAN = '#5ad8ff'
const CYAN_CORE = '#e8fbff'
const GOLD = '#f6b500'

function faces(cx: number, cy: number, s: number) {
  const w = s * 0.866
  return {
    outline: `M${cx} ${cy - s}L${cx + w} ${cy - s / 2}L${cx + w} ${cy + s / 2}L${cx} ${cy + s}L${cx - w} ${cy + s / 2}L${cx - w} ${cy - s / 2}Z`,
    top: `M${cx} ${cy - s}L${cx + w} ${cy - s / 2}L${cx} ${cy}L${cx - w} ${cy - s / 2}Z`,
    left: `M${cx - w} ${cy - s / 2}L${cx} ${cy}L${cx} ${cy + s}L${cx - w} ${cy + s / 2}Z`,
    right: `M${cx} ${cy}L${cx + w} ${cy - s / 2}L${cx + w} ${cy + s / 2}L${cx} ${cy + s}Z`,
  }
}

const CX = -8
const CY = -118
const SIZE = 104

function PluginCore() {
  const f = faces(CX, CY, SIZE)
  return <g transform="translate(233 645)" strokeLinejoin="round">
    <g className="creator-fx-hover">
      <Glow blur={30} className="creator-fx-halo"><path d={faces(CX, CY, SIZE * 1.12).outline} fill="#7d9cff" /></Glow>
      <path d={f.outline} fill="#0b1430" stroke="#fff" strokeWidth="24" />
      <g data-craft-part="core" className="creator-fx-core" opacity="0">
        <Glow blur={22}><path d={faces(CX, CY, SIZE * 0.8).outline} fill={CYAN} /></Glow>
        <path d={faces(CX, CY, SIZE * 0.46).outline} fill={CYAN_CORE} />
      </g>
      <g data-craft-part="left" className="creator-fx-part-left"><path d={f.left} fill="#3161f7" /></g>
      <g data-craft-part="right" className="creator-fx-part-right"><path d={f.right} fill="#1d3aa8" /></g>
      <g data-craft-part="top" className="creator-fx-part-top">
        <path d={f.top} fill="#a9bdff" />
        <ellipse cx={CX} cy={CY - SIZE / 2} rx="22" ry="12" fill={GOLD} />
      </g>
    </g>
    <g data-craft-part="burst" className="creator-fx-burst" opacity="0" style={{ transformOrigin: `${CX}px ${CY}px` }}>
      <circle cx={CX} cy={CY} r="150" fill="none" stroke={CYAN_CORE} strokeWidth="14" />
      {[[0, -1], [1, 0], [0, 1], [-1, 0]].map(([x, y]) => <path key={`${x}${y}`} d={`M${CX + x * 196} ${CY + y * 196 - 26}l9 17 17 9-17 9-9 17-9-17-17-9 17-9Z`} fill={GOLD} stroke="#fff" strokeWidth="6" />)}
    </g>
  </g>
}

function ids(prefix: string) {
  const id = useId().replace(/:/g, '')
  return (name: string) => `creator-${prefix}-${name}-${id}`
}

function Light({ name, glowOpacity = .85, coreOpacity = 1, effect = 'dca-fx-pulse', core = CYAN_CORE, children }: { name: string; glowOpacity?: number; coreOpacity?: number; effect?: string; core?: string; children: ReactNode }) {
  const filter = ids('visor')(name)
  return <g className={`dca-part-face dca-part-face-${name}`} data-mask-face={name}>
    <defs><filter id={filter} x="-30%" y="-60%" width="160%" height="220%"><feGaussianBlur stdDeviation="16" /></filter></defs>
    <g className={`dca-fx ${effect}`} filter={`url(#${filter})`} fill={CYAN} stroke={CYAN} opacity={glowOpacity}>{children}</g>
    <g className="dca-part-eyes creator-fx-visor-eyes" fill={core} stroke={core} opacity={coreOpacity}>{children}</g>
  </g>
}

function HudVisorPlate() {
  const id = ids('plate')
  return <g data-faceplate="creator:hud-visor">
    <defs>
      <clipPath id={id('clip')}><rect x="307" y="476" width="410" height="291" rx="68" /></clipPath>
      <linearGradient id={id('fill')} x1="0" y1="476" x2="0" y2="767" gradientUnits="userSpaceOnUse"><stop offset="0" stopColor="#1d2848" /><stop offset="1" stopColor="#090e1c" /></linearGradient>
    </defs>
    <rect x="307" y="476" width="410" height="291" rx="68" fill={`url(#${id('fill')})`} />
    <g clipPath={`url(#${id('clip')})`}>
      <path d="M307 476h410v70q-205-44-410 0Z" fill="#3a4a7a" opacity=".5" />
      <g className="dca-fx dca-fx-scan"><rect x="324" y="476" width="56" height="291" fill={CYAN} opacity=".16" /></g>
    </g>
  </g>
}

const EYE_L = 'M388 598l18-26h48l18 26v44l-18 24h-48l-18-24Z'
const EYE_R = 'M552 598l18-26h48l18 26v44l-18 24h-48l-18-24Z'

function HudVisorEyes() {
  return <>
    <Light name="neutral"><path d={EYE_L} /><path d={EYE_R} /></Light>
    <Light name="happy" glowOpacity={1} effect="dca-fx-flare">
      <path d="M388 640l42-50 42 50-16 14-26-30-26 30Z" /><path d="M552 640l42-50 42 50-16 14-26-30-26 30Z" />
    </Light>
    <Light name="operator" glowOpacity={.75} effect="dca-fx-none">
      <path d="M388 616l14-14h56l14 14v30h-84Z" /><path d="M552 616l14-14h56l14 14v30h-84Z" />
    </Light>
    <Light name="sleep" glowOpacity={.3} coreOpacity={.4} core={CYAN} effect="dca-fx-breathe">
      <rect x="388" y="648" width="84" height="12" rx="6" /><rect x="552" y="648" width="84" height="12" rx="6" />
    </Light>
  </>
}

const CREATOR_PACK: AvatarComponentPack = {
  namespace: 'creator',
  label: 'Creator',
  items: [
    { id: 'creator:plugin-core', slot: 'hand', rarity: 'legendary', series: 'creator:workshop', zones: ['hand'], workStory: 'craft', render: PluginCore },
    { id: 'creator:hud-visor', slot: 'face', kind: 'faceplate', rarity: 'legendary', series: 'creator:workshop', zones: ['screen'], renderPlate: HudVisorPlate, renderEyes: HudVisorEyes },
  ],
}

const CREATOR_OUTFIT_WEAR = { hand: 'creator:plugin-core', face: 'creator:hud-visor' } as unknown as AvatarOutfitWear

export const CREATOR_MASCOT_OUTFIT = { pack: CREATOR_PACK, wear: CREATOR_OUTFIT_WEAR } as const

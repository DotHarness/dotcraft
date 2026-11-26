import { useId } from 'react'
import type { MascotOutfitProps } from '@dotcraft/plugin'
import { CLEF } from './clef'

const INK = '#23252c'
const GOLD = '#f2b630'
const GLEAM = '#fff6bd'
const TIP = { x: 226, y: 392 }

const CLEF_ANCHOR = { x: 60, y: 250 }
const CLEF_PATH = `M 70 150 ${CLEF}`
// Line widths in clef units: 1.4px on the 58px Composer mascot and the intro's 3px at 112px.
const LINE = 15.5

function Contoured({ d, fill, contour = 14 }: { d: string; fill: string; contour?: number }): JSX.Element {
  return <path d={d} fill={fill} stroke="#fff" strokeWidth={contour} strokeLinejoin="round" paintOrder="stroke fill" />
}

function TipGlow(): JSX.Element {
  const id = `oratorio-baton-glow-${useId().replace(/:/g, '')}`
  return (
    <>
      <defs>
        <radialGradient id={id}>
          <stop offset="0" stopColor={GLEAM} />
          <stop offset=".35" stopColor={GOLD} stopOpacity=".7" />
          <stop offset="1" stopColor={GOLD} stopOpacity="0" />
        </radialGradient>
      </defs>
      <circle data-conduct-part="glow" cx={TIP.x} cy={TIP.y} r="132" fill={`url(#${id})`} opacity="0" style={{ transformOrigin: `${TIP.x}px ${TIP.y}px` }} />
    </>
  )
}

function RisingClef(): JSX.Element {
  return (
    <g transform={`translate(${TIP.x} ${TIP.y}) rotate(24) scale(1.6) translate(${-CLEF_ANCHOR.x} ${-CLEF_ANCHOR.y})`} fill="none" strokeLinecap="round" strokeLinejoin="round">
      <g data-conduct-part="clef" opacity="0">
        <path d={CLEF_PATH} stroke={GOLD} strokeWidth={LINE * 3.4} opacity=".24" />
        <path d={CLEF_PATH} stroke={GOLD} strokeWidth={LINE} />
        <path d={CLEF_PATH} stroke={GLEAM} strokeWidth={LINE * .45} opacity=".85" />
      </g>
    </g>
  )
}

function Baton(): JSX.Element {
  return (
    <g transform="translate(226 644) rotate(-24) translate(-226 -644)" strokeLinecap="round">
      <TipGlow />
      <path d="M226 616V404" stroke="#fff" strokeWidth="34" />
      <path d="M226 616V404" stroke={GOLD} strokeWidth="14" />
      <path d="M221 590V420" stroke={GLEAM} strokeWidth="4" opacity=".8" />
      <circle cx="226" cy="392" r="22" fill={GLEAM} stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <circle cx="226" cy="392" r="10" fill={GOLD} />
      <Contoured d="M208 606a18 18 0 0 1 36 0v50a18 18 0 0 1-36 0Z" fill={INK} contour={16} />
      <RisingClef />
    </g>
  )
}

export const CONDUCTOR_OUTFIT: Omit<MascotOutfitProps, 'children'> = {
  pack: {
    namespace: 'oratorio',
    label: 'Oratorio',
    items: [
      { id: 'oratorio:baton', slot: 'hand', rarity: 'rare', series: 'costume', zones: ['hand'], workStory: 'conduct', render: Baton }
    ]
  },
  wear: { hand: 'oratorio:baton' }
}

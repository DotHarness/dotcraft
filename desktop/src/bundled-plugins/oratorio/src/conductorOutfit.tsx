import type { MascotOutfitProps } from '@dotcraft/plugin'

const INK = '#23252c'
const SATIN = '#454a56'
const GOLD = '#f2b630'
const GLEAM = '#fff6bd'

function Contoured({ d, fill, contour = 14 }: { d: string; fill: string; contour?: number }): JSX.Element {
  return <path d={d} fill={fill} stroke="#fff" strokeWidth={contour} strokeLinejoin="round" paintOrder="stroke fill" />
}

function BowTie(): JSX.Element {
  return (
    <>
      <Contoured d="M512 816 602 788q18-5 18 13v58q0 18-18 13L512 844 422 872q-18 5-18-13v-58q0-18 18-13Z" fill={INK} />
      <path d="M560 810 598 800M560 850 598 860M464 810 426 800M464 850 426 860" stroke={SATIN} strokeWidth="7" strokeLinecap="round" />
      <circle cx="512" cy="830" r="22" fill={SATIN} stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
    </>
  )
}

function Baton(): JSX.Element {
  return (
    <g transform="translate(226 644) rotate(-24) translate(-226 -644)" strokeLinecap="round">
      <path d="M226 616V404" stroke="#fff" strokeWidth="34" />
      <path d="M226 616V404" stroke={GOLD} strokeWidth="14" />
      <path d="M221 590V420" stroke={GLEAM} strokeWidth="4" opacity=".8" />
      <circle cx="226" cy="392" r="22" fill={GLEAM} stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <circle cx="226" cy="392" r="10" fill={GOLD} />
      <Contoured d="M208 606a18 18 0 0 1 36 0v50a18 18 0 0 1-36 0Z" fill={INK} contour={16} />
    </g>
  )
}

export const CONDUCTOR_OUTFIT: Omit<MascotOutfitProps, 'children'> = {
  pack: {
    namespace: 'oratorio',
    label: 'Oratorio',
    items: [
      { id: 'oratorio:bow-tie', slot: 'face', kind: 'accessory', rarity: 'rare', series: 'costume', zones: ['rim'], render: BowTie },
      { id: 'oratorio:baton', slot: 'hand', rarity: 'rare', series: 'costume', zones: ['hand'], workStory: 'conduct', render: Baton }
    ]
  },
  wear: { face: 'oratorio:bow-tie', hand: 'oratorio:baton' }
}

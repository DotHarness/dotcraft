import type { JSX } from 'react'

function ProfileFace({ kind, mark, accent }: { kind: number; mark: string; accent: string }) {
  return <g className={kind === 0 ? undefined : 'dca-part-eyes'} data-profile-face={kind} strokeLinecap="round" strokeLinejoin="round">
    {kind === 0 && <><g className="dca-part-eyes"><path d="M387 568 477 634 387 700" stroke={mark} strokeWidth="38" /></g><path className="dca-part-caret" d="M531 696h116" stroke={accent} strokeWidth="27" /></>}
    {kind === 1 && <><path d="M379 585 452 622 379 659M645 585 572 622 645 659" stroke={mark} strokeWidth="30" /><path d="M482 704c18 11 42 11 60 0" stroke={mark} strokeWidth="22" /></>}
    {kind === 2 && <><path d="M389 612h58M577 612h58" stroke={mark} strokeWidth="30" /><path d="M493 700h42" stroke={mark} strokeWidth="20" /></>}
    {kind === 3 && <><rect x="381" y="581" width="45" height="87" rx="16" fill={mark} /><rect x="598" y="581" width="45" height="87" rx="16" fill={mark} /><path d="M487 700h50" stroke={mark} strokeWidth="22" /></>}
    {kind === 4 && <><path d="M366 586h88v46M570 586h88v46" stroke={mark} strokeWidth="28" /><path d="M481 700h62" stroke={mark} strokeWidth="22" /></>}
  </g>
}
export function Faces({ mark, accent, baseFace = 0 }: { mark: string; accent: string; baseFace?: number }): JSX.Element {
  return (
    <>
      <g className="dca-part-face dca-part-face-neutral"><ProfileFace kind={baseFace} mark={mark} accent={accent} /></g>
      <g className="dca-part-face dca-part-face-happy">
        <g className="dca-part-eyes">
          <path d="M379 585 452 622 379 659" stroke={mark} strokeWidth="30" strokeLinecap="round" strokeLinejoin="round" />
          <path d="M645 585 572 622 645 659" stroke={mark} strokeWidth="30" strokeLinecap="round" strokeLinejoin="round" />
        </g>
        <path d="M470 702c20 18 64 18 84 0" stroke={accent} strokeWidth="24" strokeLinecap="round" fill="none" />
      </g>
      <g className="dca-part-face dca-part-face-operator">
        <g className="dca-part-eyes">
          <rect x="381" y="581" width="45" height="87" rx="16" fill={mark} />
          <rect x="598" y="581" width="45" height="87" rx="16" fill={mark} />
        </g>
        <path d="M487 700h50" stroke={accent} strokeWidth="22" strokeLinecap="round" />
      </g>
      <g className="dca-part-face dca-part-face-sleep">
        <path d="M373 618c26 20 56 20 82 0" stroke={mark} strokeWidth="26" strokeLinecap="round" fill="none" />
        <path d="M569 618c26 20 56 20 82 0" stroke={mark} strokeWidth="26" strokeLinecap="round" fill="none" />
        <path d="M488 704h48" stroke={accent} strokeWidth="20" strokeLinecap="round" />
      </g>
    </>
  )
}

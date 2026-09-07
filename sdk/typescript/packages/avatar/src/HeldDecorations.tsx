import type { HeldId } from './appearanceModel.js'

/** The short grip meets the unchanged left hand at (233, 675). */
export function HeldDecoration({ id }: { id: HeldId }) {
  return <g data-held-decoration={id} strokeLinecap="round" strokeLinejoin="round">
    <path d="M233 671v55" stroke="#fff" strokeWidth="25" />
    <path d="M233 671v55" stroke="#687987" strokeWidth="11" />
    {id === 'task-board' && <>
      <rect x="143" y="707" width="171" height="143" rx="18" fill="#f3e7cc" stroke="#fff" strokeWidth="12" />
      <path d="M160 828h137" stroke="#d3bea0" strokeWidth="9" />
      <rect x="202" y="696" width="55" height="25" rx="8" fill="#85989e" stroke="#fff" strokeWidth="6" />
      <path d="m164 749 10 10 18-22M213 749h71M167 792h117" stroke="#7c887d" strokeWidth="11" fill="none" />
    </>}
    {id === 'wrench' && <>
      <path d="M216 703v49c-30 10-48 40-39 70l25-27 24 22-26 26c33 10 66-9 75-40 7-23-3-46-23-57v-43Z" fill="#aebcc8" stroke="#fff" strokeWidth="13" />
      <path d="M235 718v39c22 8 29 27 22 42" stroke="#708796" strokeWidth="10" fill="none" />
    </>}
    {id === 'shield' && <>
      <path d="m233 703 87 30-8 57c-6 42-41 70-79 85-38-15-73-43-79-85l-8-57Z" fill="#7198b2" stroke="#fff" strokeWidth="13" />
      <path d="M233 720v136c32-17 59-39 63-70l6-41Z" fill="#547c99" />
      <path d="m190 780 28 26 51-59" stroke="#fff" strokeWidth="16" fill="none" />
    </>}
    {id === 'magnifier' && <>
      <path d="M233 695v49" stroke="#fff" strokeWidth="28" /><path d="M233 695v49" stroke="#475d71" strokeWidth="16" />
      <circle cx="233" cy="790" r="59" fill="#adcfdc" fillOpacity=".75" stroke="#fff" strokeWidth="22" />
      <circle cx="233" cy="790" r="59" stroke="#526a7d" strokeWidth="13" fill="none" />
      <path d="M205 770q16-19 37-15" stroke="#f4fbfc" strokeWidth="11" fill="none" />
    </>}
    {id === 'control-panel' && <>
      <rect x="139" y="713" width="182" height="129" rx="21" fill="#475a70" stroke="#fff" strokeWidth="13" />
      <path d="M157 823h146" stroke="#293f53" strokeWidth="10" />
      <path d="M167 753h127M167 797h127" stroke="#b4c4d3" strokeWidth="10" />
      <circle cx="198" cy="753" r="15" fill="#8acbbb" stroke="#354c60" strokeWidth="5" />
      <circle cx="263" cy="797" r="15" fill="#f0c86b" stroke="#354c60" strokeWidth="5" />
    </>}
  </g>
}

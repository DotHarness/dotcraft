import type { HeldId } from './appearanceModel.js'

export function HeldDecoration({ id }: { id: HeldId }) {
  return <g data-held-decoration={id} strokeLinecap="round" strokeLinejoin="round">
    {id === 'task-board' && <g transform="translate(233 640) scale(.8) translate(-840 -665)"><g>
    <rect x="718" y="562" width="244" height="194" rx="36" fill="#fff" stroke="#fff" strokeWidth="30" />
    <rect x="718" y="562" width="244" height="194" rx="36" fill="#fff" stroke="var(--dca-held-mark, #3161f7)" strokeWidth="11" />
    <path d="M760 620h114" stroke="var(--dca-held-mark, #3161f7)" strokeWidth="18" strokeLinecap="round" />
    <circle cx="775" cy="689" r="14" fill="var(--dca-held-mark, #3161f7)" />
    <circle cx="842" cy="661" r="14" fill="var(--dca-held-accent, #f6b500)" />
    <circle cx="907" cy="708" r="14" fill="var(--dca-held-mark, #3161f7)" />
    <path d="M789 683 828 667 893 699" stroke="#202124" strokeWidth="13" strokeLinecap="round" strokeLinejoin="round" opacity=".72" />
  </g>
</g>}
    {id === 'wrench' && <g transform="translate(233 670) rotate(-60) scale(.65) translate(-642 -817)"><g>
    <path d="M756 595c30-30 74-39 113-23l-56 56 46 46 56-56c16 39 7 83-23 113-32 32-80 39-119 20L674 850c-18 18-47 18-65 0s-18-47 0-65l99-99c-19-39-12-87 20-119Z" fill="#fff" stroke="#fff" strokeWidth="34" strokeLinejoin="round" />
    <path d="M756 595c30-30 74-39 113-23l-56 56 46 46 56-56c16 39 7 83-23 113-32 32-80 39-119 20L674 850c-18 18-47 18-65 0s-18-47 0-65l99-99c-19-39-12-87 20-119Z" fill="#fff" stroke="var(--dca-held-mark, #3161f7)" strokeWidth="13" strokeLinejoin="round" />
    <circle cx="642" cy="817" r="17" fill="var(--dca-held-accent, #f6b500)" />
    <path d="M706 752 774 684" stroke="var(--dca-held-mark, #3161f7)" strokeWidth="20" strokeLinecap="round" opacity=".78" />
  </g>
</g>}
    {id === 'shield' && <g transform="translate(233 635) scale(.8) translate(-817 -690)"><g>
    <path d="M817 553 929 594v76c0 80-46 134-112 162-66-28-112-82-112-162v-76Z" fill="#fff" stroke="#fff" strokeWidth="30" strokeLinejoin="round" />
    <path d="M817 553 929 594v76c0 80-46 134-112 162-66-28-112-82-112-162v-76Z" fill="#fff" stroke="var(--dca-held-mark, #3161f7)" strokeWidth="11" strokeLinejoin="round" />
    <path d="M761 683 802 722 881 632" stroke="var(--dca-held-mark, #3161f7)" strokeWidth="28" strokeLinecap="round" strokeLinejoin="round" />
  </g>
</g>}
    {id === 'magnifier' && <g transform="translate(233 670) rotate(-18) scale(.85) translate(-862 -735)"><g>
    <g>
      <path d="M862 646v112" stroke="#fff" strokeWidth="48" strokeLinecap="round" />
      <path d="M862 646v112" stroke="var(--dca-held-mark, #3161f7)" strokeWidth="28" strokeLinecap="round" />
      <circle cx="862" cy="562" r="82" fill="#fff" stroke="#fff" strokeWidth="30" />
      <circle cx="862" cy="562" r="82" fill="#fff" fillOpacity=".72" stroke="var(--dca-held-mark, #3161f7)" strokeWidth="18" />
      <path d="M828 534c21-22 54-30 82-19" stroke="#fff" strokeWidth="12" strokeLinecap="round" opacity=".9" />
    </g>
  </g>
</g>}
    {id === 'control-panel' && <g transform="translate(233 640) scale(.8) translate(-839 -686)"><g>
    <rect x="714" y="592" width="250" height="190" rx="38" fill="#fff" stroke="#fff" strokeWidth="30" />
    <rect x="714" y="592" width="250" height="190" rx="38" fill="#fff" stroke="var(--dca-held-mark, #3161f7)" strokeWidth="11" />
    <path d="M765 650h148" stroke="#202124" strokeWidth="14" strokeLinecap="round" opacity=".62" />
    <path d="M765 718h148" stroke="#202124" strokeWidth="14" strokeLinecap="round" opacity=".62" />
    <circle cx="818" cy="650" r="23" fill="var(--dca-held-mark, #3161f7)" stroke="#fff" strokeWidth="8" />
    <circle cx="872" cy="718" r="23" fill="var(--dca-held-accent, #f6b500)" stroke="#fff" strokeWidth="8" />
  </g>
</g>}
  </g>
}

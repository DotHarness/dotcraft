import type { HandId } from './items.js'
import { Glow } from './DecorationShapes.js'

const mark = 'var(--dca-held-mark, #3161f7)'
const accent = 'var(--dca-held-accent, #f6b500)'

export function HandDecoration({ id }: { id: HandId }) {
  return <g data-held-decoration={id} strokeLinecap="round" strokeLinejoin="round">
    {id === 'task-board' && <g transform="translate(233 640) scale(.8) translate(-840 -665)">
      <rect x="718" y="562" width="244" height="194" rx="36" fill="#fff" stroke="#fff" strokeWidth="30" />
      <rect x="718" y="562" width="244" height="194" rx="36" fill="#fff" stroke={mark} strokeWidth="11" />
      <path d="M760 620h114" stroke={mark} strokeWidth="18" />
      <circle cx="775" cy="689" r="14" fill={mark} />
      <circle cx="842" cy="661" r="14" fill={accent} />
      <circle cx="907" cy="708" r="14" fill={mark} />
      <path d="M789 683 828 667 893 699" stroke="#202124" strokeWidth="13" opacity=".72" />
    </g>}
    {id === 'wrench' && <g transform="translate(233 670) rotate(-60) scale(.65) translate(-642 -817)">
      <path d="M756 595c30-30 74-39 113-23l-56 56 46 46 56-56c16 39 7 83-23 113-32 32-80 39-119 20L674 850c-18 18-47 18-65 0s-18-47 0-65l99-99c-19-39-12-87 20-119Z" fill="#fff" stroke="#fff" strokeWidth="34" />
      <path d="M756 595c30-30 74-39 113-23l-56 56 46 46 56-56c16 39 7 83-23 113-32 32-80 39-119 20L674 850c-18 18-47 18-65 0s-18-47 0-65l99-99c-19-39-12-87 20-119Z" fill="#fff" stroke={mark} strokeWidth="13" />
      <circle cx="642" cy="817" r="17" fill={accent} />
      <path d="M706 752 774 684" stroke={mark} strokeWidth="20" opacity=".78" />
    </g>}
    {id === 'shield' && <g transform="translate(233 635) scale(.8) translate(-817 -690)">
      <path d="M817 553 929 594v76c0 80-46 134-112 162-66-28-112-82-112-162v-76Z" fill="#fff" stroke="#fff" strokeWidth="30" />
      <path d="M817 553 929 594v76c0 80-46 134-112 162-66-28-112-82-112-162v-76Z" fill="#fff" stroke={mark} strokeWidth="11" />
      <path d="M761 683 802 722 881 632" stroke={mark} strokeWidth="28" />
    </g>}
    {id === 'magnifier' && <g transform="translate(233 670) rotate(-18) scale(.85) translate(-862 -735)">
      <path d="M862 646v112" stroke="#fff" strokeWidth="48" />
      <path d="M862 646v112" stroke={mark} strokeWidth="28" />
      <circle cx="862" cy="562" r="82" fill="#fff" stroke="#fff" strokeWidth="30" />
      <circle cx="862" cy="562" r="82" fill="#fff" fillOpacity=".72" stroke={mark} strokeWidth="18" />
      <path d="M828 534c21-22 54-30 82-19" stroke="#fff" strokeWidth="12" opacity=".9" />
    </g>}
    {id === 'control-panel' && <g transform="translate(233 640) scale(.8) translate(-839 -686)">
      <rect x="714" y="592" width="250" height="190" rx="38" fill="#fff" stroke="#fff" strokeWidth="30" />
      <rect x="714" y="592" width="250" height="190" rx="38" fill="#fff" stroke={mark} strokeWidth="11" />
      <path d="M765 650h148" stroke="#202124" strokeWidth="14" opacity=".62" />
      <path d="M765 718h148" stroke="#202124" strokeWidth="14" opacity=".62" />
      <circle cx="818" cy="650" r="23" fill={mark} stroke="#fff" strokeWidth="8" />
      <circle cx="872" cy="718" r="23" fill={accent} stroke="#fff" strokeWidth="8" />
    </g>}
    {id === 'coffee-mug' && <g transform="translate(233 640) scale(1.05) translate(-800 -705)">
      <g className="dca-fx dca-fx-steam" fill="none" stroke="#b9c2d1" strokeWidth="10" opacity=".75">
        <path d="M772 628c10-14-2-26 8-40" /><path d="M800 620c10-14-2-26 8-40" /><path d="M828 628c10-14-2-26 8-40" />
      </g>
      <path d="M852 676a34 34 0 1 1 0 68" fill="none" stroke="#fff" strokeWidth="34" />
      <path d="M852 676a34 34 0 1 1 0 68" fill="none" stroke={mark} strokeWidth="13" />
      <rect x="748" y="650" width="104" height="112" rx="18" fill="#fff" stroke="#fff" strokeWidth="30" />
      <rect x="748" y="650" width="104" height="112" rx="18" fill="#fff" stroke={mark} strokeWidth="11" />
      <rect x="748" y="694" width="104" height="22" fill={mark} />
      <ellipse cx="800" cy="656" rx="42" ry="10" fill="#5a3c2a" />
    </g>}
    {id === 'magic-wand' && <g transform="translate(233 640) rotate(-20) scale(1.1) translate(-800 -720)">
      <g className="dca-fx dca-fx-sparkle" fill="#fff3c4">
        <path d="M748 560l6 14 14 6-14 6-6 14-6-14-14-6 14-6Z" />
        <path d="M856 556l5 11 11 5-11 5-5 11-5-11-11-5 11-5Z" style={{ animationDelay: '.6s' }} />
        <path d="M838 636l4 9 9 4-9 4-4 9-4-9-9-4 9-4Z" style={{ animationDelay: '1.1s' }} />
      </g>
      <path d="M800 604v168" stroke="#fff" strokeWidth="40" />
      <path d="M800 604v168" stroke="#3b2f5c" strokeWidth="22" />
      <path d="M800 551 808 574 832 574 813 589 820 612 800 599 780 612 787 589 768 574 792 574Z" fill="#f6b500" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
    </g>}
    {id === 'energy-blade' && <g transform="translate(233 640) rotate(-12) scale(.9) translate(-800 -760)">
      <g className="dca-fx-blade" style={{ transformOrigin: '800px 716px' }}>
        <Glow blur={14} className="dca-fx-pulse"><rect className="dca-fx-blade-color" x="778" y="420" width="44" height="300" rx="22" fill="#4de3ff" opacity=".7" /></Glow>
        <rect className="dca-fx-blade-color" x="786" y="420" width="28" height="298" rx="14" fill="#4de3ff" stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
        <rect x="795" y="432" width="10" height="276" rx="5" fill="#e6fbff" />
      </g>
      <rect x="777" y="712" width="46" height="18" rx="6" fill="#8b95a5" stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
      <rect x="783" y="726" width="34" height="90" rx="12" fill="#2a3140" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
    </g>}
    {id === 'paintbrush' && <g transform="translate(233 640) rotate(-25) translate(-800 -720)">
      <rect x="788" y="660" width="24" height="180" rx="12" fill="#e0ad84" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <rect x="780" y="626" width="40" height="40" rx="6" fill="#8b95a5" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
      <path d="M778 630h44l6-50h-56Z" fill="#f1c8a2" stroke="#fff" strokeWidth="12" strokeLinejoin="round" paintOrder="stroke fill" />
      <path d="M774 598h52l2-18h-56Z" fill={mark} />
    </g>}
    {id === 'boba-tea' && <g transform="translate(233 640) scale(1.2) translate(-800 -712)">
      <rect x="812" y="576" width="16" height="100" rx="8" fill={accent} stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
      <path d="M758 640h84l-10 140h-64Z" fill="#fff" stroke="#fff" strokeWidth="14" strokeLinejoin="round" paintOrder="stroke fill" />
      <path d="M762 696h76l-6 84h-64Z" fill="#e0ad84" />
      <g fill="#3c4658"><circle cx="782" cy="764" r="8" /><circle cx="800" cy="768" r="8" /><circle cx="818" cy="764" r="8" /><circle cx="791" cy="748" r="8" /></g>
      <ellipse cx="800" cy="640" rx="46" ry="10" fill="#fff" stroke={mark} strokeWidth="6" />
    </g>}
    {id === 'gamepad' && <g transform="translate(233 640) scale(1.05) translate(-800 -695)">
      <rect x="720" y="650" width="160" height="92" rx="40" fill="#fff" stroke="#fff" strokeWidth="30" />
      <rect x="720" y="650" width="160" height="92" rx="40" fill="#fff" stroke={mark} strokeWidth="11" />
      <path d="M752 690h12v-12h14v12h12v14h-12v12h-14v-12h-12Z" fill={mark} />
      <circle cx="834" cy="684" r="10" fill={accent} /><circle cx="856" cy="704" r="10" fill={mark} />
    </g>}
    {id === 'flag' && <g transform="translate(233 640) rotate(-8) scale(.95) translate(-800 -760)">
      <g className="dca-fx-wave" style={{ transformOrigin: '808px 600px' }}>
        <path d="M808 566 934 602 808 640Z" fill={mark} stroke="#fff" strokeWidth="14" strokeLinejoin="round" paintOrder="stroke fill" />
      </g>
      <rect x="794" y="556" width="14" height="270" rx="7" fill="#8b95a5" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
    </g>}
    {id === 'lantern' && <g transform="translate(233 640) scale(1.05) translate(-800 -690)">
      <path d="M776 604a24 24 0 0 1 48 0" fill="none" stroke="#3c4658" strokeWidth="10" strokeLinecap="round" />
      <rect x="752" y="616" width="96" height="124" rx="32" fill="#e8654f" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <Glow blur={14} className="dca-fx-pulse"><rect x="776" y="640" width="48" height="76" rx="18" fill="#ffd970" opacity=".9" /></Glow>
      <rect x="776" y="640" width="48" height="76" rx="18" fill="#ffd970" />
      <rect x="770" y="604" width="60" height="20" rx="6" fill="#3c4658" /><rect x="770" y="732" width="60" height="16" rx="6" fill="#3c4658" />
    </g>}
    {id === 'staff' && <g transform="translate(233 640) rotate(-10) translate(-800 -780)">
      <rect x="792" y="560" width="16" height="284" rx="8" fill="#8a6520" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <Glow blur={16} className="dca-fx-pulse"><circle className="dca-fx-orb-color" cx="800" cy="544" r="40" fill="#8b5cf6" opacity=".8" /></Glow>
      <circle className="dca-fx-orb-color" cx="800" cy="544" r="34" fill="#8b5cf6" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
      <circle cx="788" cy="532" r="10" fill="#fff" opacity=".85" />
    </g>}
  </g>
}

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
    {id === 'thunder-hammer' && <g transform="translate(233 640) rotate(-12) scale(.86) translate(-800 -700)">
      <Glow blur={16} className="dca-fx-pulse"><rect x="708" y="456" width="184" height="120" rx="28" fill="#4de3ff" opacity=".55" /></Glow>
      <rect x="787" y="556" width="26" height="186" rx="12" fill="#3c4658" stroke="#fff" strokeWidth="16" paintOrder="stroke fill" />
      <rect x="778" y="734" width="44" height="28" rx="10" fill="#8b95a5" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <rect x="715" y="466" width="170" height="100" rx="20" fill="#aab4c3" stroke="#fff" strokeWidth="18" paintOrder="stroke fill" />
      <path d="M735 466h14v100h-14q-20 0-20-20v-60q0-20 20-20ZM865 466h-14v100h14q20 0 20-20v-60q0-20-20-20Z" fill="#737e90" />
      <rect x="777" y="466" width="46" height="100" fill={mark} />
      {['M740 458l-24-26 26-8-18-40', 'M860 458l18-28-26-6 26-36', 'M712 510l-22 2 12 14-14 10'].map((d, index) =>
        <g key={d} className="dca-fx dca-fx-node" fill="none" strokeLinejoin="miter" style={index ? { animationDelay: `${index * .6}s` } : undefined}>
          <path d={d} stroke="#4de3ff" strokeWidth="20" /><path d={d} stroke="#f2feff" strokeWidth="8" />
        </g>)}
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
    {id === 'umbrella' && <g transform="translate(233 640) rotate(-14) scale(.85) translate(-800 -708)">
      <path d="M800 366V770a22 22 0 0 1-44 0" stroke="#fff" strokeWidth="36" />
      <path d="M800 366V770a22 22 0 0 1-44 0" stroke="#3c4658" strokeWidth="16" />
      <path d="M695 490c0-64 47-104 105-104s105 40 105 104a35 22 0 0 0-70 0a35 22 0 0 0-70 0a35 22 0 0 0-70 0Z" fill="#e8654f" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <path d="M800 386c-20 22-34 60-35 104a35 22 0 0 1 70 0c-1-44-15-82-35-104Z" fill="#b94f50" />
    </g>}
    {id === 'pizza-slice' && <g transform="translate(233 640) rotate(-10) scale(.9) translate(-800 -707)">
      <g fill="#fff" stroke="#fff" strokeWidth="28"><path d="M800 500 876 700H724Z" /><rect x="714" y="688" width="172" height="38" rx="19" /></g>
      <path d="M800 500 876 700H724Z" fill="#f3cf62" />
      <rect x="714" y="688" width="172" height="38" rx="19" fill="#e0ad84" />
      <g fill="#e8654f"><circle cx="800" cy="578" r="17" /><circle cx="774" cy="650" r="17" /><circle cx="826" cy="640" r="17" /></g>
    </g>}
    {id === 'sunflower' && <g transform="translate(233 640) rotate(-12) translate(-800 -780)">
      <rect x="792" y="600" width="16" height="240" rx="8" fill="#89b875" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <path d="M792 716c-14-34-44-48-74-40 8 32 38 50 74 40Z" fill="#89b875" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
      <path d="M800 531a13 13 0 1 1 22 6a13 13 0 1 1 16 16a13 13 0 1 1 6 22a13 13 0 1 1-6 22a13 13 0 1 1-16 16a13 13 0 1 1-22 6a13 13 0 1 1-22-6a13 13 0 1 1-16-16a13 13 0 1 1-6-22a13 13 0 1 1 6-22a13 13 0 1 1 16-16a13 13 0 1 1 22-6Z" fill="#f6b500" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <circle cx="800" cy="575" r="26" fill="#805840" />
    </g>}
    {id === 'camera' && <g transform="translate(233 640) scale(1.05) translate(-800 -695)">
      <g fill="#fff" stroke="#fff" strokeWidth="30"><rect x="740" y="624" width="56" height="36" rx="12" /><rect x="838" y="630" width="30" height="24" rx="8" /><rect x="720" y="646" width="160" height="104" rx="28" /></g>
      <rect x="740" y="624" width="56" height="36" rx="12" fill="#fff" stroke={mark} strokeWidth="11" />
      <rect x="838" y="630" width="30" height="24" rx="8" fill={accent} />
      <rect x="720" y="646" width="160" height="104" rx="28" fill="#fff" stroke={mark} strokeWidth="11" />
      <circle cx="812" cy="700" r="36" fill="#fff" stroke={mark} strokeWidth="18" />
      <circle cx="812" cy="700" r="12" fill={mark} />
    </g>}
    {id === 'binoculars' && <g transform="translate(233 640) scale(.95) translate(-800 -700)">
      <g fill="#fff" stroke="#fff" strokeWidth="28"><rect x="743" y="598" width="36" height="40" rx="10" /><rect x="821" y="598" width="36" height="40" rx="10" /><rect x="728" y="626" width="144" height="128" rx="24" /></g>
      <rect x="784" y="648" width="32" height="44" rx="8" fill="#3c4658" />
      <g fill="#3c4658"><rect x="743" y="598" width="36" height="40" rx="10" /><rect x="821" y="598" width="36" height="40" rx="10" /></g>
      <g fill="#537f59"><rect x="728" y="626" width="64" height="128" rx="24" /><rect x="808" y="626" width="64" height="128" rx="24" /></g>
      <g fill="#a2c5d1"><circle cx="760" cy="716" r="22" /><circle cx="840" cy="716" r="22" /></g>
    </g>}
    {id === 'goldfish-bag' && <g transform="translate(233 640) rotate(6) scale(1.1) translate(-800 -644)">
      <path d="M800 648 782 606h36Z" fill="#a2c5d1" stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
      <path d="M792 642h16c4 22 54 30 54 84 0 30-28 46-62 46s-62-16-62-46c0-54 50-62 54-84Z" fill="#a2c5d1" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <path d="M792 642h16c3 17 32 25 46 51H746c14-26 43-34 46-51Z" fill="#c7f6ff" />
      <path d="M764 730c0-11 13-18 29-18 12 0 22 6 27 13l20-14v38l-20-14c-5 7-15 13-27 13-16 0-29-7-29-18Z" fill="#ed985f" />
    </g>}
    {id === 'megaphone' && <g transform="translate(233 640) rotate(-26) translate(-800 -700)">
      <path d="M818 618l26 30" stroke="#fff" strokeWidth="44" />
      <path d="M818 618l26 30" stroke={mark} strokeWidth="22" />
      <path d="M784 664 740 520h120l-44 144Z" fill="#fff" stroke="#fff" strokeWidth="30" />
      <path d="M784 664 740 520h120l-44 144Z" fill="#fff" stroke={mark} strokeWidth="11" />
      <path d="M761 590h78l-7 24h-64Z" fill={mark} />
      <ellipse cx="800" cy="520" rx="60" ry="18" fill="#fff" stroke={mark} strokeWidth="16" />
      <rect x="782" y="660" width="36" height="30" rx="8" fill={accent} stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
    </g>}
    {id === 'parrot' && <g transform="translate(233 640) translate(-800 -640)">
      <path d="M780 610 764 740h36l16-126Z" fill="#4fae6a" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <path d="M802 510c32 0 48 28 48 64 0 42-22 70-52 70-28 0-44-26-44-62 0-42 18-72 48-72Z" fill="#4fae6a" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <g className="dca-fx-swing" style={{ transformOrigin: '800px 516px' }}>
        <path d="M824 474c16-2 28 10 28 26-2 6-6 8-10 4-2-8-8-12-18-12Z" fill="#f6b500" stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
        <circle cx="802" cy="490" r="30" fill="#e8654f" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
        <circle cx="812" cy="484" r="6" fill="#202124" />
      </g>
    </g>}
    {id === 'firefly-jar' && <g transform="translate(233 640) scale(1.12) translate(-800 -705)">
      <rect x="744" y="646" width="112" height="120" rx="26" fill="#c7f6ff" fillOpacity=".6" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <rect x="752" y="626" width="96" height="28" rx="8" fill="#8b95a5" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
      <Glow blur={10} className="dca-fx-pulse"><g fill="#d9ff5a"><circle cx="776" cy="702" r="24" /><circle cx="826" cy="684" r="24" /><circle cx="808" cy="738" r="24" /></g></Glow>
      <g fill="#d9ff5a">
        <circle className="dca-fx dca-fx-node" cx="776" cy="702" r="13" /><circle className="dca-fx dca-fx-node" cx="826" cy="684" r="13" style={{ animationDelay: '.6s' }} /><circle className="dca-fx dca-fx-node" cx="808" cy="738" r="13" style={{ animationDelay: '1.2s' }} />
      </g>
    </g>}
    {id === 'trophy' && <g transform="translate(233 640) rotate(-6) scale(.8) translate(-800 -706)">
      <Glow blur={16} className="dca-fx-pulse"><path d="M750 540h100v30c0 44-22 72-50 72s-50-28-50-72Z" fill="#ffd970" opacity=".85" /></Glow>
      <path d="M752 562c-34-4-40 50 4 56M848 562c34-4 40 50-4 56" stroke="#fff" strokeWidth="36" />
      <path d="M752 562c-34-4-40 50 4 56M848 562c34-4 40 50-4 56" stroke="#c99139" strokeWidth="14" />
      <rect x="788" y="632" width="24" height="48" fill="#c99139" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
      <path d="M750 540h100v30c0 44-22 72-50 72s-50-28-50-72Z" fill="#efc65c" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <rect x="754" y="676" width="92" height="34" rx="8" fill="#3c4658" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <g className="dca-fx dca-fx-sparkle" fill="#fff3c4">
        <path d="M742 508l5 12 12 5-12 5-5 12-5-12-12-5 12-5Z" />
        <path d="M864 520l4 10 10 4-10 4-4 10-4-10-10-4 10-4Z" style={{ animationDelay: '.6s' }} />
        <path d="M806 490l4 9 9 4-9 4-4 9-4-9-9-4 9-4Z" style={{ animationDelay: '1.1s' }} />
      </g>
    </g>}
  </g>
}

import type { HandId } from './items.js'
import { Glow, useClipId } from './DecorationShapes.js'

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
    {id === 'walkie-talkie' && <g transform="translate(233 640) scale(1.05) translate(-800 -695)">
      <g fill="#fff" stroke="#fff" strokeWidth="30"><rect x="814" y="572" width="26" height="72" rx="13" /><rect x="760" y="600" width="28" height="28" rx="8" /><rect x="752" y="624" width="96" height="164" rx="26" /></g>
      <rect x="814" y="572" width="26" height="72" rx="13" fill="#3c4658" />
      <rect x="760" y="600" width="28" height="28" rx="8" fill={accent} />
      <rect x="752" y="624" width="96" height="164" rx="26" fill="#fff" stroke={mark} strokeWidth="11" />
      <rect x="772" y="648" width="56" height="66" rx="12" fill={mark} />
      <path d="M786 666h28M786 681h28M786 696h28" stroke="#fff" strokeWidth="7" />
      <circle cx="800" cy="748" r="13" fill={mark} />
    </g>}
    {id === 'potion' && <g transform="translate(233 640) scale(1.1) translate(-800 -640)">
      <g fill="#fff" stroke="#fff" strokeWidth="26"><rect x="780" y="578" width="40" height="34" rx="10" /><rect x="784" y="606" width="32" height="52" rx="6" /><circle cx="800" cy="690" r="52" /></g>
      <rect x="784" y="606" width="32" height="52" rx="6" fill="#f4f8ff" stroke="#a9b8cc" strokeWidth="8" />
      <circle cx="800" cy="690" r="52" fill="#f4f8ff" stroke="#a9b8cc" strokeWidth="8" />
      <path d="M756.7 682A44 44 0 1 0 843.3 682Z" fill="#e05aa8" />
      <g fill="#f7b8d9"><circle cx="786" cy="712" r="6" /><circle cx="816" cy="698" r="4" /></g>
      <path d="M766 670c4-10 12-18 22-22" stroke="#fff" strokeWidth="9" />
      <rect x="780" y="578" width="40" height="34" rx="10" fill="#c49b5e" />
    </g>}
    {id === 'genie-lamp' && <g transform="translate(233 650) scale(1.12) translate(-800 -650)">
      <path d="M804 640c30-20 56 0 50 22-4 18-26 22-48 12" stroke="#fff" strokeWidth="38" />
      <path d="M804 640c30-20 56 0 50 22-4 18-26 22-48 12" stroke="#c99139" strokeWidth="16" />
      <g fill="#efc65c" stroke="#fff" strokeWidth="14" paintOrder="stroke fill">
        <path d="M743 686h32l9 16h-50Z" />
        <path d="M718 648C706 648 698 638 692 620l-14 8c6 24 20 42 44 50Z" />
        <ellipse cx="759" cy="656" rx="50" ry="32" />
        <path d="M731 632c2-18 12-30 28-30s26 12 28 30Z" />
        <circle cx="759" cy="592" r="10" />
      </g>
      <path d="M711 662q48 20 96 0" stroke="#c99139" strokeWidth="9" />
      <path d="M733 640q26-8 52 0" stroke="#fff3c4" strokeWidth="7" />
    </g>}
    {id === 'lollipop' && <g transform="translate(233 640) rotate(-6) translate(-800 -800)">
      <rect x="791" y="640" width="18" height="240" rx="9" fill="#fff" stroke="#fff" strokeWidth="26" />
      <rect x="791" y="640" width="18" height="240" rx="9" fill="#fff" stroke="#c9d1dd" strokeWidth="5" />
      <circle cx="800" cy="639" r="80" fill="#f2a0b4" stroke="#fff" strokeWidth="16" paintOrder="stroke fill" />
      <path d="M800 639a10 10 0 0 1 20 0a20 20 0 0 1-40 0a30 30 0 0 1 60 0a40 40 0 0 1-80 0a50 50 0 0 1 100 0a60 60 0 0 1-120 0" stroke="#e8654f" strokeWidth="15" />
      <path d="M752 596c10-12 24-20 40-22" stroke="#fff" strokeWidth="10" opacity=".8" />
    </g>}
    {id === 'pickaxe' && <g transform="translate(233 640) rotate(-12) scale(.85) translate(-800 -800)">
      <rect x="789" y="548" width="22" height="318" rx="11" fill="#e0ad84" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <rect x="786" y="796" width="28" height="60" rx="8" fill="#f6b500" />
      <path d="M696 600C712 540 754 506 800 506S888 540 904 600C874 574 838 562 800 562S726 574 696 600Z" fill="#8b95a5" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <path d="M726 562c20-24 46-38 74-38s54 14 74 38" stroke="#c3cad6" strokeWidth="9" />
      <rect x="778" y="504" width="44" height="62" rx="10" fill="#5d6778" stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
    </g>}
    {id === 'bug-net' && <g transform="translate(233 640) translate(-800 -640)">
      <path d="M836 432 791 690" stroke="#fff" strokeWidth="34" />
      <path d="M836 432 791 690" stroke={mark} strokeWidth="18" />
      <path d="M697 440C694 500 716 552 750 562S812 540 826 500C832 482 836 460 838 424Z" fill="#fff" fillOpacity=".9" stroke="#fff" strokeWidth="14" strokeLinejoin="round" />
      <path d="M720 446C722 500 736 540 750 556M752 440C754 500 754 540 752 560M784 438C784 500 774 540 758 558M812 434C810 490 792 530 766 556M704 482Q765 502 832 472M714 522Q760 538 818 514" stroke="#b9c2d1" strokeWidth="4" />
      <g className="dca-fx-swing" style={{ transformOrigin: '760px 518px' }}>
        <path d="M752 492c-4-10-12-14-20-12M768 492c4-10 12-14 20-12M742 512h-16M742 528h-16M778 512h16M778 528h16" stroke="#2b2f3a" strokeWidth="6" />
        <circle cx="760" cy="498" r="11" fill="#2b2f3a" />
        <ellipse cx="760" cy="522" rx="20" ry="24" fill="#2b2f3a" />
        <path d="M760 502v44" stroke="#5b6577" strokeWidth="4" />
        <ellipse cx="752" cy="514" rx="4" ry="7" fill="#fff" opacity=".7" />
      </g>
      <ellipse cx="767" cy="432" rx="72" ry="30" transform="rotate(-8 767 432)" stroke="#fff" strokeWidth="32" />
      <ellipse cx="767" cy="432" rx="72" ry="30" transform="rotate(-8 767 432)" stroke={mark} strokeWidth="14" />
    </g>}
    {id === 'hot-pepper' && <g transform="translate(233 640) scale(1.1) translate(-800 -640)">
      <Glow blur={12} className="dca-fx-pulse"><ellipse cx="744" cy="452" rx="34" ry="44" fill="#ffb347" opacity=".8" /></Glow>
      <g className="dca-fx dca-fx-flame" style={{ transformOrigin: '742px 488px' }}>
        <path d="M742 488C712 476 706 446 722 420c4 14 12 20 20 20-4-22 6-40 24-50-4 24 10 38 12 58 2 22-14 36-36 40Z" fill="#ffb347" stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
        <path d="M744 480c-14-6-18-22-10-36 4 8 10 10 14 10 0-12 4-22 12-28-2 14 6 24 6 36s-8 20-22 18Z" fill="#ffe08a" />
      </g>
      <path d="M808 656c4 16 14 26 30 28" stroke="#fff" strokeWidth="28" />
      <path d="M808 656c4 16 14 26 30 28" stroke="#3f8f4f" strokeWidth="12" />
      <path d="M762 640C742 604 732 566 736 530c2-20 0-32-6-44-4-8 4-14 12-8 22 14 40 44 52 72 14 32 34 54 46 80Z" fill="#e8654f" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <path d="M776 612c-12-24-20-50-22-80" stroke="#fff" strokeWidth="9" opacity=".55" />
      <path d="M756 648c-2-18 16-30 44-30s48 12 46 30c-14 12-76 12-90 0Z" fill="#4fae6a" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
    </g>}
    {id === 'plasma-globe' && <g transform="translate(233 640) translate(-800 -640)">
      <path d="M756 684h88l-10-62h-68Z" fill="#1d2433" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <rect x="772" y="602" width="56" height="22" rx="6" fill="#3c4658" stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
      <circle cx="800" cy="540" r="70" fill="#34245c" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
      <Glow blur={14} className="dca-fx-pulse"><circle cx="800" cy="540" r="40" fill="#c084fc" opacity=".8" /></Glow>
      {['M800 540l-14-22 12-8-18-26', 'M800 540l24-10 2-16 24-8', 'M800 540l-26 6 4 14-28 8', 'M800 540l14 22 16-4 10 22'].map((d, index) =>
        <g key={d} className="dca-fx dca-fx-node" fill="none" style={index ? { animationDelay: `${index * .45}s` } : undefined}>
          <path d={d} stroke="#c084fc" strokeWidth="12" /><path d={d} stroke="#f5d0fe" strokeWidth="5" />
        </g>)}
      <circle cx="800" cy="540" r="15" fill="#f5d0fe" />
      <path d="M752 508c8-16 22-28 40-32" stroke="#fff" strokeWidth="9" opacity=".7" />
    </g>}
    {id === 'master-key' && <MasterKey />}
    {id === 'pet-dragon' && <PetDragon />}
  </g>
}

function MasterKey() {
  const clip = useClipId()
  const shape = 'M728 422H818V712H782V508H728V486H746V470H728V452H750V436H728Z'
  const bow = 'M800 718a54 54 0 1 1 0 108 54 54 0 1 1 0-108Zm0 34a20 20 0 1 0 0 40 20 20 0 1 0 0-40Z'
  return <g transform="translate(233 640) rotate(-12) scale(.9) translate(-800 -760)">
    <defs><clipPath id={clip}><path d={shape} /><path d={bow} /></clipPath></defs>
    <Glow blur={16} className="dca-fx-pulse"><path d="M772 410h56v326h-56ZM716 412h70v108h-70Z" fill="#ffd970" opacity=".8" /></Glow>
    <path d={shape} fill="#efc65c" stroke="#fff" strokeWidth="16" strokeLinejoin="round" paintOrder="stroke fill" />
    <path d={bow} fill="#efc65c" fillRule="evenodd" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
    <path d="M812 432v270" stroke="#c99139" strokeWidth="7" />
    <rect x="774" y="700" width="52" height="26" rx="8" fill="#c99139" stroke="#fff" strokeWidth="10" paintOrder="stroke fill" />
    <g clipPath={`url(#${clip})`}><g transform="translate(800 740) rotate(-90) scale(.45 1)"><g className="dca-fx dca-fx-sheen" fill="#fff3c4"><path d="M3-80h67l-73 160h-67Z" opacity=".9" /></g></g></g>
  </g>
}

const dragonWing = 'M0 0C-10-60-50-110-100-128Q-84-104-92-80Q-72-86-60-66Q-40-66-30-40Q-14-30 0 0Z'
const dragonFarWing = 'M0 0C-4-60-20-110-42-138Q-40-112-54-92Q-40-88-34-66Q-22-62-18-40Q-8-26 0 0Z'
function PetDragon() {
  return <g transform="translate(233 640) translate(-800 -640)">
    {[{ at: 'translate(790 552)', d: dragonWing }, { at: 'translate(814 552) scale(-1 1)', d: dragonFarWing }].map(wing =>
      <g key={wing.at} transform={wing.at}><g className="dca-fx-flap-slow" style={{ transformOrigin: '0px 0px' }}>
        <path d={wing.d} fill="#2f7a4f" stroke="#fff" strokeWidth="12" strokeLinejoin="round" paintOrder="stroke fill" />
      </g></g>)}
    <path d="M824 604C852 636 852 688 820 710c-10 6-18-2-10-10 22-20 22-50 2-78Z" fill="#4fae6a" stroke="#fff" strokeWidth="12" paintOrder="stroke fill" />
    <path d="M812 700l-26 6 18 18Z" fill="#2f7a4f" stroke="#fff" strokeWidth="8" strokeLinejoin="round" paintOrder="stroke fill" />
    <path d="M806 532c34 0 48 34 46 64-2 34-24 54-50 54s-40-20-38-54c2-30 14-64 42-64Z" fill="#4fae6a" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
    <ellipse cx="786" cy="600" rx="16" ry="34" fill="#f6e3a1" />
    <path d="M788 644v14M812 646v14" stroke="#fff" strokeWidth="22" />
    <path d="M788 644v14M812 646v14" stroke="#2f7a4f" strokeWidth="10" />
    <path d="M812 464 840 424 826 468ZM796 460 806 416 786 460Z" fill="#e0ad84" stroke="#fff" strokeWidth="8" strokeLinejoin="round" paintOrder="stroke fill" />
    <path d="M826 490c0-22-16-38-38-38-18 0-32 8-40 20-10 4-18 10-18 20 0 10 10 16 24 16 14 10 30 14 46 12 16-4 26-14 26-30Z" fill="#4fae6a" stroke="#fff" strokeWidth="14" paintOrder="stroke fill" />
    <circle cx="794" cy="480" r="7" fill="#202124" /><circle cx="740" cy="490" r="3" fill="#202124" />
    <g transform="translate(728 496) rotate(-80)">
      <Glow blur={10} className="dca-fx-pulse"><ellipse cy="-30" rx="18" ry="32" fill="#ffb347" opacity=".75" /></Glow>
      <g className="dca-fx dca-fx-flame" style={{ transformOrigin: '0px 0px' }}>
        <path d="M0 0C-16-6-22-24-14-40c2 10 8 14 14 14-4-14 2-28 12-36-4 16 6 26 6 40S10 0 0 0Z" fill="#ffb347" stroke="#fff" strokeWidth="8" paintOrder="stroke fill" />
        <path d="M1-8c-8-4-10-14-6-20 2 4 6 6 8 6 0-8 2-14 6-18 0 10 4 16 4 22s-4 12-12 10Z" fill="#ffe08a" />
      </g>
    </g>
  </g>
}

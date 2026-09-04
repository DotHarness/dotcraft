import { useId, type JSX } from 'react'
import { paletteOf, type AvatarSpec } from './agentAvatar'

interface RobotAvatarProps {
  spec: AvatarSpec
  size?: number
  /** Subtle idle bob; used on the big builder header / intro mascot. */
  animated?: boolean
}

/**
 * Terminal-screen face marks shared with MascotRobot and Agent Profile avatars.
 * `mark` = role-mark gradient ref, `yellow` = brand yellow gradient ref.
 */
function Face({ kind, mark, yellow }: { kind: number; mark: string; yellow: string }): JSX.Element {
  switch (kind % 5) {
    case 1: // happy ( > < + smile )
      return (
        <g>
          <path d="M379 585 452 622 379 659" stroke={mark} strokeWidth="30" strokeLinecap="round" strokeLinejoin="round" />
          <path d="M645 585 572 622 645 659" stroke={mark} strokeWidth="30" strokeLinecap="round" strokeLinejoin="round" />
          <path d="M482 704c18 11 42 11 60 0" stroke={mark} strokeWidth="22" strokeLinecap="round" fill="none" />
        </g>
      )
    case 2: // curious ( two dashes + small dash )
      return (
        <g strokeLinecap="round">
          <path d="M389 612h58" stroke={mark} strokeWidth="30" />
          <path d="M577 612h58" stroke={mark} strokeWidth="30" />
          <path d="M493 700h42" stroke={mark} strokeWidth="20" />
        </g>
      )
    case 3: // operator ( rounded-rect eyes + dash )
      return (
        <g>
          <rect x="381" y="581" width="45" height="87" rx="16" fill={mark} />
          <rect x="598" y="581" width="45" height="87" rx="16" fill={mark} />
          <path d="M487 700h50" stroke={mark} strokeWidth="22" strokeLinecap="round" />
        </g>
      )
    case 4: // skeptical ( bracket eyes + mouth )
      return (
        <g fill="none" strokeLinecap="round" strokeLinejoin="round">
          <path d="M366 586h88v46" stroke={mark} strokeWidth="28" />
          <path d="M570 586h88v46" stroke={mark} strokeWidth="28" />
          <path d="M481 700h62" stroke={mark} strokeWidth="22" />
        </g>
      )
    default: // prompt ( > caret eye + yellow underscore )
      return (
        <g>
          <path d="M387 568 477 634 387 700" stroke={mark} strokeWidth="38" strokeLinecap="round" strokeLinejoin="round" />
          <path d="M531 696h116" stroke={yellow} strokeWidth="27" strokeLinecap="round" />
        </g>
      )
  }
}

/** Corner accessory shared by the Agent Profile role avatars. */
function Accessory({ kind, mark, yellow, lift }: { kind: number; mark: string; yellow: string; lift: string }): JSX.Element | null {
  // "Create" sparkle — used only by the explicit Agent Builder character (AGENT_BUILDER_AVATAR).
  // avatarFromSeed/randomAvatar use `% ACCESSORY_COUNT` (0–5), so seeded avatars never get this.
  if (kind === 6) {
    return (
      <g filter={lift}>
        <path d="M700 372 716 414 758 430 716 446 700 488 684 446 642 430 684 414Z" fill={mark} stroke="#fff" strokeWidth="26" strokeLinejoin="round" />
        <path d="M842 150 884 258 992 300 884 342 842 450 800 342 692 300 800 258Z" fill={yellow} stroke="#fff" strokeWidth="38" strokeLinejoin="round" />
        <path d="M842 150 884 258 992 300 884 342 842 450 800 342 692 300 800 258Z" fill={yellow} stroke={mark} strokeWidth="10" strokeLinejoin="round" />
      </g>
    )
  }
  if (kind % 6 === 0) return null
  switch (kind % 6) {
    case 1: // plan board (leader)
      return (
        <g transform="rotate(6 838 665)" filter={lift}>
          <rect x="718" y="562" width="244" height="194" rx="36" fill="#fff" stroke="#fff" strokeWidth="30" />
          <rect x="718" y="562" width="244" height="194" rx="36" fill="#fff" stroke={mark} strokeWidth="11" />
          <path d="M760 620h114" stroke={mark} strokeWidth="18" strokeLinecap="round" />
          <circle cx="775" cy="689" r="14" fill={mark} />
          <circle cx="842" cy="661" r="14" fill={yellow} />
          <circle cx="907" cy="708" r="14" fill={mark} />
          <path d="M789 683 828 667 893 699" stroke="#202124" strokeWidth="13" strokeLinecap="round" strokeLinejoin="round" opacity=".72" />
        </g>
      )
    case 2: // wrench (builder)
      return (
        <g transform="translate(122 -140) rotate(-18 826 706)" filter={lift}>
          <path d="M756 595c30-30 74-39 113-23l-56 56 46 46 56-56c16 39 7 83-23 113-32 32-80 39-119 20L674 850c-18 18-47 18-65 0s-18-47 0-65l99-99c-19-39-12-87 20-119Z" fill="#fff" stroke="#fff" strokeWidth="34" strokeLinejoin="round" />
          <path d="M756 595c30-30 74-39 113-23l-56 56 46 46 56-56c16 39 7 83-23 113-32 32-80 39-119 20L674 850c-18 18-47 18-65 0s-18-47 0-65l99-99c-19-39-12-87 20-119Z" fill="#fff" stroke={mark} strokeWidth="13" strokeLinejoin="round" />
          <circle cx="642" cy="817" r="17" fill={yellow} />
          <path d="M706 752 774 684" stroke={mark} strokeWidth="20" strokeLinecap="round" opacity=".78" />
        </g>
      )
    case 3: // shield check (reviewer)
      return (
        <g transform="translate(58 -52) rotate(8 820 674)" filter={lift}>
          <path d="M817 553 929 594v76c0 80-46 134-112 162-66-28-112-82-112-162v-76Z" fill="#fff" stroke="#fff" strokeWidth="30" strokeLinejoin="round" />
          <path d="M817 553 929 594v76c0 80-46 134-112 162-66-28-112-82-112-162v-76Z" fill="#fff" stroke={mark} strokeWidth="11" strokeLinejoin="round" />
          <path d="M761 683 802 722 881 632" stroke={mark} strokeWidth="28" strokeLinecap="round" strokeLinejoin="round" />
        </g>
      )
    case 4: // magnifier (explorer)
      return (
        <g transform="translate(22 -22) rotate(28 862 646)" filter={lift}>
          <path d="M862 646v112" stroke="#fff" strokeWidth="48" strokeLinecap="round" />
          <path d="M862 646v112" stroke={mark} strokeWidth="28" strokeLinecap="round" />
          <circle cx="862" cy="562" r="82" fill="#fff" stroke="#fff" strokeWidth="30" />
          <circle cx="862" cy="562" r="82" fill="#fff" fillOpacity=".72" stroke={mark} strokeWidth="18" />
          <path d="M828 534c21-22 54-30 82-19" stroke="#fff" strokeWidth="12" strokeLinecap="round" opacity=".9" />
        </g>
      )
    default: // control panel (operator)
      return (
        <g transform="rotate(7 839 686)" filter={lift}>
          <rect x="714" y="592" width="250" height="190" rx="38" fill="#fff" stroke="#fff" strokeWidth="30" />
          <rect x="714" y="592" width="250" height="190" rx="38" fill="#fff" stroke={mark} strokeWidth="11" />
          <path d="M765 650h148" stroke="#202124" strokeWidth="14" strokeLinecap="round" opacity=".62" />
          <path d="M765 718h148" stroke="#202124" strokeWidth="14" strokeLinecap="round" opacity=".62" />
          <circle cx="818" cy="650" r="23" fill={mark} stroke="#fff" strokeWidth="8" />
          <circle cx="872" cy="718" r="23" fill={yellow} stroke="#fff" strokeWidth="8" />
        </g>
      )
  }
}

export function RobotAvatar({ spec, size = 40, animated = false }: RobotAvatarProps): JSX.Element {
  const uid = useId().replace(/:/g, '')
  const p = paletteOf(spec)
  const body = `body-${uid}`
  const mark = `mark-${uid}`
  const yellow = `yellow-${uid}`
  const soft = `soft-${uid}`
  const inner = `inner-${uid}`
  const lift = `lift-${uid}`

  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 1024 1024"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      role="img"
      aria-label="agent avatar"
      className={`ab-robot${animated ? ' ab-robot--anim' : ''}`}
    >
      <defs>
        <linearGradient id={body} x1="279" y1="766" x2="736" y2="334" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor={p.bodyD} />
          <stop offset=".5" stopColor={p.bodyM} />
          <stop offset="1" stopColor={p.bodyL} />
        </linearGradient>
        <linearGradient id={mark} x1="366" y1="694" x2="658" y2="560" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor={p.markD} />
          <stop offset="1" stopColor={p.markL} />
        </linearGradient>
        <linearGradient id={yellow} x1="481" y1="174" x2="617" y2="713" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#ffcf11" />
          <stop offset="1" stopColor="#f6b500" />
        </linearGradient>
        <filter id={soft} x="-12%" y="-12%" width="124%" height="124%">
          <feDropShadow dx="0" dy="18" stdDeviation="24" floodColor={p.shadow} floodOpacity=".16" />
        </filter>
        <filter id={inner} x="-8%" y="-8%" width="116%" height="116%">
          <feDropShadow dx="0" dy="10" stdDeviation="16" floodColor={p.shadow} floodOpacity=".1" />
        </filter>
        <filter id={lift} x="-18%" y="-18%" width="136%" height="136%">
          <feDropShadow dx="0" dy="14" stdDeviation="14" floodColor="#020617" floodOpacity=".18" />
        </filter>
      </defs>

      <g transform="translate(512 528) scale(1.3) translate(-512 -512)">
        <g filter={`url(#${soft})`}>
          <rect x="201" y="365" width="622" height="513" rx="151" fill="#fff" />
          <rect x="145" y="472" width="136" height="256" rx="58" fill="#fff" />
          <rect x="743" y="472" width="136" height="256" rx="58" fill="#fff" />
          <rect x="438" y="270" width="148" height="182" rx="2" fill="#fff" />
          <circle cx="512" cy="229" r="116" fill="#fff" />
        </g>

        <g filter={`url(#${inner})`}>
          <rect x="243" y="408" width="538" height="426" rx="113" fill={`url(#${body})`} />
          <rect x="188" y="514" width="90" height="171" rx="19" fill={`url(#${body})`} />
          <rect x="746" y="514" width="90" height="171" rx="19" fill={`url(#${body})`} />
          <rect x="479" y="337" width="66" height="119" rx="6" fill={`url(#${body})`} />
        </g>

        <rect x="295" y="464" width="434" height="315" rx="78" fill="#fff" />
        <circle cx="512" cy="229" r="73" fill={`url(#${yellow})`} />

        <Face kind={spec.face} mark={`url(#${mark})`} yellow={`url(#${yellow})`} />
      </g>

      <Accessory kind={spec.accessory} mark={`url(#${mark})`} yellow={`url(#${yellow})`} lift={`url(#${lift})`} />
    </svg>
  )
}

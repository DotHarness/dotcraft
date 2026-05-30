import { useId, type CSSProperties, type JSX } from 'react'

/**
 * Inline DotCraft mascot robot with a swappable face.
 *
 * The body is the standard blue DotCraft robot (mirrors resources/dotcraft.svg);
 * only the face marks inside the white "terminal screen" change per expression,
 * reusing the same visual vocabulary as the Teams role avatars (assets/teams/*).
 */

export type MascotExpression = 'neutral' | 'happy' | 'operator'

/**
 * Antenna "status light" colour. Semantic state per the visual spec:
 * `error` → `--error`, `success` → `--success`, `default` → the brand yellow.
 */
export type MascotLight = 'default' | 'error' | 'success'

interface MascotRobotProps {
  expression?: MascotExpression
  light?: MascotLight
  size?: number
  className?: string
  style?: CSSProperties
}

function Face({ expression, mark, accent }: {
  expression: MascotExpression
  mark: string
  accent: string
}): JSX.Element {
  switch (expression) {
    case 'happy':
      return (
        <g>
          <path d="M379 585 452 622 379 659" stroke={mark} strokeWidth="30" strokeLinecap="round" strokeLinejoin="round" />
          <path d="M645 585 572 622 645 659" stroke={mark} strokeWidth="30" strokeLinecap="round" strokeLinejoin="round" />
          <path d="M470 702c20 18 64 18 84 0" stroke={accent} strokeWidth="24" strokeLinecap="round" fill="none" />
        </g>
      )
    case 'operator':
      return (
        <g>
          <rect x="381" y="581" width="45" height="87" rx="16" fill={mark} />
          <rect x="598" y="581" width="45" height="87" rx="16" fill={mark} />
          <path d="M487 700h50" stroke={accent} strokeWidth="22" strokeLinecap="round" />
        </g>
      )
    case 'neutral':
    default:
      return (
        <g>
          <path d="M387 568 477 634 387 700" stroke={mark} strokeWidth="38" strokeLinecap="round" strokeLinejoin="round" />
          <path d="M531 696h116" stroke={accent} strokeWidth="27" strokeLinecap="round" />
        </g>
      )
  }
}

export function MascotRobot({
  expression = 'neutral',
  light = 'default',
  size = 48,
  className,
  style
}: MascotRobotProps): JSX.Element {
  const uid = useId().replace(/:/g, '')
  const blue = `mascot-blue-${uid}`
  const blueMark = `mascot-blue-mark-${uid}`
  const yellow = `mascot-yellow-${uid}`
  const softShadow = `mascot-soft-shadow-${uid}`
  const innerLift = `mascot-inner-lift-${uid}`

  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 1024 1024"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className}
      style={style}
      role="img"
      aria-label="DotCraft mascot"
    >
      <defs>
        <linearGradient id={blue} x1="279" y1="766" x2="736" y2="334" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#2458f7" />
          <stop offset=".46" stopColor="#5f82f7" />
          <stop offset="1" stopColor="#8fa5ff" />
        </linearGradient>
        <linearGradient id={blueMark} x1="380" y1="696" x2="492" y2="557" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#2257f5" />
          <stop offset=".55" stopColor="#577df7" />
          <stop offset="1" stopColor="#8ca2ff" />
        </linearGradient>
        <linearGradient id={yellow} x1="481" y1="174" x2="617" y2="713" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#ffcf11" />
          <stop offset="1" stopColor="#f6b500" />
        </linearGradient>
        <filter id={softShadow} x="-12%" y="-12%" width="124%" height="124%">
          <feDropShadow dx="0" dy="18" stdDeviation="24" floodColor="#0b3d62" floodOpacity=".18" />
        </filter>
        <filter id={innerLift} x="-8%" y="-8%" width="116%" height="116%">
          <feDropShadow dx="0" dy="10" stdDeviation="16" floodColor="#163a88" floodOpacity=".1" />
        </filter>
      </defs>

      <g transform="translate(512 528) scale(1.3) translate(-512 -512)">
        <g filter={`url(#${softShadow})`}>
          <rect x="201" y="365" width="622" height="513" rx="151" fill="#fff" />
          <rect x="145" y="472" width="136" height="256" rx="58" fill="#fff" />
          <rect x="743" y="472" width="136" height="256" rx="58" fill="#fff" />
          <rect x="438" y="270" width="148" height="182" rx="2" fill="#fff" />
          <circle cx="512" cy="229" r="116" fill="#fff" />
        </g>

        <g filter={`url(#${innerLift})`}>
          <rect x="243" y="408" width="538" height="426" rx="113" fill={`url(#${blue})`} />
          <rect x="188" y="514" width="90" height="171" rx="19" fill={`url(#${blue})`} />
          <rect x="746" y="514" width="90" height="171" rx="19" fill={`url(#${blue})`} />
          <rect x="479" y="337" width="66" height="119" rx="6" fill={`url(#${blue})`} />
        </g>

        <rect x="295" y="464" width="434" height="315" rx="78" fill="#fff" />
        <circle
          cx="512"
          cy="229"
          r="73"
          fill={light === 'error' ? 'var(--error)' : light === 'success' ? 'var(--success)' : `url(#${yellow})`}
        />

        <Face expression={expression} mark={`url(#${blueMark})`} accent={`url(#${yellow})`} />
      </g>
    </svg>
  )
}

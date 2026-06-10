import { useId, type CSSProperties, type JSX } from 'react'

/**
 * Inline DotCraft mascot robot with a swappable face.
 *
 * The body is the standard blue DotCraft robot (mirrors resources/dotcraft.svg);
 * only the face marks inside the white "terminal screen" change per expression,
 * reusing the same visual vocabulary as the Teams role avatars (assets/teams/*).
 *
 * Animation hooks: the paint order stays exactly the brand rendering (one soft
 * drop shadow under the union of all white shapes, one inner lift under the
 * union of all blue shapes — group-level filters keep internal seams shadow-free),
 * but the arm white/blue pieces, antenna pieces, and face marks carry stable
 * `mascot-*` classes so tokens.css can animate them. Arm pieces are two separate
 * elements per arm that rotate in lockstep around a shared shoulder origin
 * (213,530 / 811,530 in viewBox space, via `transform-box: view-box`); the static
 * "shoulder pad" circles stay behind a raised arm and keep the blue mass connected
 * to the body (ball joint). The `mascot-glow` halo behind the antenna ball is
 * transparent at rest and only lit by state animations.
 */

export type MascotExpression = 'neutral' | 'happy' | 'operator' | 'sleep'

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
        <g className="mascot-face">
          <g className="mascot-eyes">
            <path d="M379 585 452 622 379 659" stroke={mark} strokeWidth="30" strokeLinecap="round" strokeLinejoin="round" />
            <path d="M645 585 572 622 645 659" stroke={mark} strokeWidth="30" strokeLinecap="round" strokeLinejoin="round" />
          </g>
          <path d="M470 702c20 18 64 18 84 0" stroke={accent} strokeWidth="24" strokeLinecap="round" fill="none" />
        </g>
      )
    case 'operator':
      return (
        <g className="mascot-face">
          <g className="mascot-eyes">
            <rect x="381" y="581" width="45" height="87" rx="16" fill={mark} />
            <rect x="598" y="581" width="45" height="87" rx="16" fill={mark} />
          </g>
          <path d="M487 700h50" stroke={accent} strokeWidth="22" strokeLinecap="round" />
        </g>
      )
    case 'sleep':
      // Closed-eye arcs; same mark/accent vocabulary as the other faces.
      return (
        <g className="mascot-face">
          <path d="M373 618c26 20 56 20 82 0" stroke={mark} strokeWidth="26" strokeLinecap="round" fill="none" />
          <path d="M569 618c26 20 56 20 82 0" stroke={mark} strokeWidth="26" strokeLinecap="round" fill="none" />
          <path d="M488 704h48" stroke={accent} strokeWidth="20" strokeLinecap="round" />
        </g>
      )
    case 'neutral':
    default:
      // Terminal prompt face: the "mouth" is a caret, so idle blinking flashes
      // it like a cursor (see composer-mascot-blink in tokens.css).
      return (
        <g className="mascot-face">
          <g className="mascot-eyes">
            <path d="M387 568 477 634 387 700" stroke={mark} strokeWidth="38" strokeLinecap="round" strokeLinejoin="round" />
          </g>
          <path className="mascot-caret" d="M531 696h116" stroke={accent} strokeWidth="27" strokeLinecap="round" />
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
  const lightFill =
    light === 'error' ? 'var(--error)' : light === 'success' ? 'var(--success)' : `url(#${yellow})`
  const glowFill = light === 'error' ? 'var(--error)' : light === 'success' ? 'var(--success)' : '#f6b500'

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
          <rect className="mascot-arm-l-w" x="145" y="472" width="136" height="256" rx="58" fill="#fff" />
          <rect className="mascot-arm-r-w" x="743" y="472" width="136" height="256" rx="58" fill="#fff" />
          <rect x="438" y="270" width="148" height="182" rx="2" fill="#fff" />
          <circle className="mascot-antenna-w" cx="512" cy="229" r="116" fill="#fff" />
        </g>

        <g filter={`url(#${innerLift})`}>
          <rect x="243" y="408" width="538" height="426" rx="113" fill={`url(#${blue})`} />
          {/* Shoulder pads: hidden inside the arm blues at rest; when an arm is
              raised they stay put and bridge body blue ↔ arm blue (ball joint). */}
          <circle cx="236" cy="546" r="30" fill={`url(#${blue})`} />
          <circle cx="788" cy="546" r="30" fill={`url(#${blue})`} />
          <rect className="mascot-arm-l-b" x="188" y="514" width="90" height="171" rx="19" fill={`url(#${blue})`} />
          <rect className="mascot-arm-r-b" x="746" y="514" width="90" height="171" rx="19" fill={`url(#${blue})`} />
          <rect x="479" y="337" width="66" height="119" rx="6" fill={`url(#${blue})`} />
        </g>

        <rect x="295" y="464" width="434" height="315" rx="78" fill="#fff" />
        <circle className="mascot-glow" cx="512" cy="229" r="96" fill={glowFill} />
        <circle className="mascot-light" cx="512" cy="229" r="73" fill={lightFill} />

        <Face expression={expression} mark={`url(#${blueMark})`} accent={`url(#${yellow})`} />
      </g>
    </svg>
  )
}

import { useId, type CSSProperties, type JSX, type ReactNode } from 'react'
import { Faces } from './Faces.js'
import { mascotPaletteOf } from './palette.js'

// Paired arm layers share view-box pivots and paint rules in rig.css.

export type MascotExpression = 'neutral' | 'happy' | 'operator' | 'sleep'

export type MascotLight = 'default' | 'error' | 'success'

interface MascotRobotProps {
  baseFace?: number
  held?: ReactNode
  accessory?: ReactNode
  top?: ReactNode
  surface?: ReactNode
  expression?: MascotExpression
  light?: MascotLight
  size?: number
  className?: string
  style?: CSSProperties
  /**
   * Recolors the body / arm / face-mark gradients from the profile's palette. The antenna
   * deliberately stays brand-yellow so its error/success status semantics survive.
   */
  avatar?: { palette: number }
}

function mixHex(a: string, b: string, amount: number): string {
  const left = parseHex(a)
  const right = parseHex(b)
  const ratio = Math.max(0, Math.min(1, amount))
  const mix = (from: number, to: number): number => Math.round(from + (to - from) * ratio)
  return `#${toHex(mix(left.r, right.r))}${toHex(mix(left.g, right.g))}${toHex(mix(left.b, right.b))}`
}

function parseHex(hex: string): { r: number; g: number; b: number } {
  const normalized = hex.trim().replace(/^#/, '')
  return {
    r: Number.parseInt(normalized.slice(0, 2), 16),
    g: Number.parseInt(normalized.slice(2, 4), 16),
    b: Number.parseInt(normalized.slice(4, 6), 16)
  }
}

function toHex(value: number): string {
  return value.toString(16).padStart(2, '0')
}

export function MascotRig({
  expression = 'neutral',
  light = 'default',
  size = 48,
  className,
  style,
  avatar, top, surface, baseFace, accessory, held
}: MascotRobotProps): JSX.Element {
  const uid = useId().replace(/:/g, '')
  const blue = `dca-part-blue-${uid}`
  const blueMark = `dca-part-blue-mark-${uid}`
  const yellow = `dca-part-yellow-${uid}`
  const softShadow = `dca-part-soft-shadow-${uid}`
  const innerLift = `dca-part-inner-lift-${uid}`
  const laptopClip = `dca-part-laptop-clip-${uid}`
  const lightFill =
    light === 'error' ? 'var(--dca-error, #dc2626)' : light === 'success' ? 'var(--dca-success, #16a34a)' : `url(#${yellow})`
  const glowFill = light === 'error' ? 'var(--dca-error, #dc2626)' : light === 'success' ? 'var(--dca-success, #16a34a)' : '#f6b500'

  const palette = mascotPaletteOf(avatar)
  const body0 = palette.bodyD
  const body1 = palette.bodyM
  const body2 = palette.bodyL
  const mark0 = palette.markD
  const mark1 = palette.markM
  const mark2 = palette.markL
  const softShadowColor = palette.shadow
  const innerLiftColor = avatar ? palette.shadow : '#163a88'
  const raisedArmLeft = avatar ? mixHex(palette.bodyD, palette.bodyM, 0.22) : '#3161f7'
  const raisedArmRight = avatar ? mixHex(palette.bodyM, palette.bodyL, 0.56) : '#7a96fb'
  const propMark = avatar ? palette.markD : '#3161f7'
  const laptopLine = palette.markL
  const svgStyle = {
    '--dca-held-mark': `url(#${blueMark})`,
    '--dca-held-accent': `url(#${yellow})`,
    '--dca-part-body-paint': `url(#${blue})`,
    '--dca-part-raised-arm-left': raisedArmLeft,
    '--dca-part-raised-arm-right': raisedArmRight,
    '--dca-part-shadow-color': softShadowColor,
    overflow: 'visible',
    ...style
  } as CSSProperties

  return (
    <svg
      width={size}
      height={size}
      viewBox="0 0 1024 1024"
      fill="none"
      xmlns="http://www.w3.org/2000/svg"
      className={className ? `dca-part-robot ${className}` : 'dca-part-robot'}
      data-expression={expression}
      style={svgStyle}
      aria-hidden="true"
    >
      <defs>
        <linearGradient className="dca-paint-body" id={blue} x1="279" y1="766" x2="736" y2="334" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor={body0} />
          <stop offset=".46" stopColor={body1} />
          <stop offset="1" stopColor={body2} />
        </linearGradient>
        <linearGradient className="dca-paint-mark" id={blueMark} x1="380" y1="696" x2="492" y2="557" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor={mark0} />
          <stop offset=".55" stopColor={mark1} />
          <stop offset="1" stopColor={mark2} />
        </linearGradient>
        <linearGradient id={yellow} x1="481" y1="174" x2="617" y2="713" gradientUnits="userSpaceOnUse">
          <stop offset="0" stopColor="#ffcf11" />
          <stop offset="1" stopColor="#f6b500" />
        </linearGradient>
        <filter id={softShadow} x="-12%" y="-12%" width="124%" height="124%">
          <feDropShadow dx="0" dy="18" stdDeviation="24" floodColor={softShadowColor} floodOpacity=".18" />
        </filter>
        <filter id={innerLift} x="-8%" y="-8%" width="116%" height="116%">
          <feDropShadow dx="0" dy="10" stdDeviation="16" floodColor={innerLiftColor} floodOpacity=".1" />
        </filter>
        <clipPath id={laptopClip}>
          <rect x="358" y="716" width="308" height="128" rx="8" />
        </clipPath>
      </defs>

      <g transform="translate(512 528) scale(1.3) translate(-512 -512)">
        <g filter={`url(#${softShadow})`}>
          <rect x="201" y="365" width="622" height="513" rx="151" fill="#fff" />
          {/* Centered on the blue band (233 / 791), not offset toward the body as the
              brand asset draws it, so the outline survives the raised-arm rotation. */}
          <rect className="dca-part-arm-l-w" x="165" y="472" width="136" height="256" rx="58" fill="#fff" />
          <rect className="dca-part-arm-r-w" x="723" y="472" width="136" height="256" rx="58" fill="#fff" />
          {!top && <rect x="438" y="270" width="148" height="182" rx="2" fill="#fff" />}
          {!top && <circle className="dca-part-antenna-w" cx="512" cy="229" r="116" fill="#fff" />}
        </g>

        <g filter={`url(#${innerLift})`}>
          <rect x="243" y="408" width="538" height="426" rx="113" fill={`url(#${blue})`} />
          <rect className="dca-part-arm-r-b" x="746" y="514" width="90" height="171" rx="19" fill={`url(#${blue})`} />
          {!top && <rect x="479" y="337" width="66" height="119" rx="6" fill={`url(#${blue})`} />}
        </g>

        {surface}
        <rect x="295" y="464" width="434" height="315" rx="78" fill="#fff" />
        {!top && <circle className="dca-part-glow" cx="512" cy="229" r="96" fill={glowFill} />}
        {top ?? <circle className="dca-part-light" cx="512" cy="229" r="73" fill={lightFill} />}

        <g className="dca-part-arm-l">
          <rect className="dca-part-arm-l-b" x="188" y="514" width="90" height="171" rx="19" fill={`url(#${blue})`} filter={`url(#${innerLift})`} />
          {held}
        </g>
        {accessory}
        <g className="dca-face-motion"><Faces baseFace={baseFace} mark={`url(#${blueMark})`} accent={`url(#${yellow})`} /></g>

        {/* A white frame would melt into the white face screen behind it, so the lid
            is dark with a white stroke ring. */}
        <g className="dca-part-prop-laptop" transform="rotate(-2.5 512 800)">
          <g filter={`url(#${softShadow})`}>
            <rect x="338" y="698" width="348" height="164" rx="16" fill="#1d2433" stroke="#fff" strokeWidth="20" />
            <rect x="300" y="862" width="424" height="26" rx="13" fill="#fff" />
          </g>
          <g clipPath={`url(#${laptopClip})`}>
            <g className="dca-part-laptop-lines" strokeLinecap="round" strokeWidth="16" fill="none">
              <path d="M382 744h118" stroke={laptopLine} />
              <path d="M382 780h170" stroke="#5fd3a6" />
              <path d="M382 816h84" stroke={laptopLine} opacity="0.75" />
              <path className="dca-part-laptop-caret" d="M478 816h30" stroke="#ffcf11" />
              <path d="M382 852h140" stroke="#5fd3a6" opacity="0.8" />
              <path d="M382 888h96" stroke={laptopLine} />
            </g>
          </g>
        </g>

        {/* Anchored to the landed hand tip of the raised right arm
            (translate(-48,88) rotate(-128°) scaleY(0.8) → tip ≈ 878,497), so the pole
            overlaps the hand. tokens.css sequences arm-first-in / sign-first-out. */}
        <g className="dca-part-prop-sign" filter={`url(#${softShadow})`}>
          <rect x="866" y="356" width="24" height="150" rx="12" fill="#fff" />
          <rect x="758" y="190" width="240" height="170" rx="26" fill="#fff" />
          <path d="M843 247a37 37 0 0 1 70 12c0 24-36 36-36 36" stroke={propMark} strokeWidth="26" fill="none" strokeLinecap="round" />
          <circle cx="878" cy="343" r="15" fill={propMark} />
        </g>
      </g>
    </svg>
  )
}

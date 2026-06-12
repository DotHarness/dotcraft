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
 * `mascot-*` classes so tokens.css can animate them.
 *
 * Arms: each arm is two elements (white "halo" capsule + blue band) that move
 * in lockstep around the shared root hinge (233,514 / 791,514 in viewBox space,
 * via `transform-box: view-box`). Three deliberate choices keep a raised arm
 * looking like a real part of the body rather than a pasted-on rectangle:
 *   1. The white capsule is centered on the blue band (not offset toward the
 *      body as the brand asset draws it), so the white outline wraps the band
 *      evenly and is still present on the underside once the arm rotates out.
 *   2. The blue band uses the body gradient at rest (a seamless continuation of
 *      the torso — the arm boundary is invisible), and tokens.css swaps its
 *      `fill` to a SOLID hinge colour (left #3161f7, right #7a96fb) only while
 *      raised (.composer-mascot-wave / -celebrate). An SVG gradient rotates with
 *      its shape, so a raised gradient arm diverges from the body's diagonal
 *      field at the seam; a solid matches the hinge colour at every angle, and
 *      the swap is masked by the motion (imperceptible at the ~44px size).
 *   3. Raised poses (wave / cheer) compose "slide then rotate, with scaleY":
 *      the translate slips the arm down so its root buries in the torso's
 *      mid-side, scaleY keeps it a short flipper, and the rotation fans the
 *      hand — so the hand grows out of the body side with no exposed joint.
 *
 * Faces: all four expressions stay mounted; `data-expression` on the root
 * `.mascot-robot` svg drives a quick fade-out → fade-in crossfade in
 * tokens.css (a terminal-screen "refresh" between glyph faces).
 *
 * Props (mini terminal, question sign) follow the same pattern: always
 * mounted at opacity 0, revealed by tokens.css via the composer root classes
 * `composer-mascot-prop-laptop` / `composer-mascot-hold-sign`. Design rule
 * (per the composer-mascot design review): props avoid new arm geometry —
 * the laptop only tucks the resting arms inward by pure translate (no
 * rotation → the gradient stays seam-continuous with the torso), and the
 * sign anchors to the landed hand tip of the existing wave raise grammar
 * (the arm poses first, then the sign fades in at its hand).
 *
 * The svg renders with `overflow: visible`: raised arms swing past the
 * 1024 viewBox edge after the 1.3× brand scale and must not be clipped.
 *
 * The `mascot-glow` halo behind the antenna ball is transparent at rest and
 * only lit by state animations.
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

/**
 * All four faces, stacked. tokens.css keeps only the one matching the svg's
 * `data-expression` visible and crossfades on switches.
 */
function Faces({ mark, accent }: { mark: string; accent: string }): JSX.Element {
  return (
    <>
      {/* Terminal prompt face: the "mouth" is a caret, so idle blinking flashes
          it like a cursor (see composer-mascot-blink in tokens.css). */}
      <g className="mascot-face mascot-face-neutral">
        <g className="mascot-eyes">
          <path d="M387 568 477 634 387 700" stroke={mark} strokeWidth="38" strokeLinecap="round" strokeLinejoin="round" />
        </g>
        <path className="mascot-caret" d="M531 696h116" stroke={accent} strokeWidth="27" strokeLinecap="round" />
      </g>
      <g className="mascot-face mascot-face-happy">
        <g className="mascot-eyes">
          <path d="M379 585 452 622 379 659" stroke={mark} strokeWidth="30" strokeLinecap="round" strokeLinejoin="round" />
          <path d="M645 585 572 622 645 659" stroke={mark} strokeWidth="30" strokeLinecap="round" strokeLinejoin="round" />
        </g>
        <path d="M470 702c20 18 64 18 84 0" stroke={accent} strokeWidth="24" strokeLinecap="round" fill="none" />
      </g>
      <g className="mascot-face mascot-face-operator">
        <g className="mascot-eyes">
          <rect x="381" y="581" width="45" height="87" rx="16" fill={mark} />
          <rect x="598" y="581" width="45" height="87" rx="16" fill={mark} />
        </g>
        <path d="M487 700h50" stroke={accent} strokeWidth="22" strokeLinecap="round" />
      </g>
      {/* Closed-eye arcs; same mark/accent vocabulary as the other faces. */}
      <g className="mascot-face mascot-face-sleep">
        <path d="M373 618c26 20 56 20 82 0" stroke={mark} strokeWidth="26" strokeLinecap="round" fill="none" />
        <path d="M569 618c26 20 56 20 82 0" stroke={mark} strokeWidth="26" strokeLinecap="round" fill="none" />
        <path d="M488 704h48" stroke={accent} strokeWidth="20" strokeLinecap="round" />
      </g>
    </>
  )
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
  const laptopClip = `mascot-laptop-clip-${uid}`
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
      className={className ? `mascot-robot ${className}` : 'mascot-robot'}
      data-expression={expression}
      style={{ overflow: 'visible', ...style }}
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
        <clipPath id={laptopClip}>
          <rect x="358" y="716" width="308" height="128" rx="8" />
        </clipPath>
      </defs>

      <g transform="translate(512 528) scale(1.3) translate(-512 -512)">
        <g filter={`url(#${softShadow})`}>
          <rect x="201" y="365" width="622" height="513" rx="151" fill="#fff" />
          {/* White "halo" capsules centered on the blue band (233 / 791) so the
              outline wraps the band evenly — survives the raised-arm rotation. */}
          <rect className="mascot-arm-l-w" x="165" y="472" width="136" height="256" rx="58" fill="#fff" />
          <rect className="mascot-arm-r-w" x="723" y="472" width="136" height="256" rx="58" fill="#fff" />
          <rect x="438" y="270" width="148" height="182" rx="2" fill="#fff" />
          <circle className="mascot-antenna-w" cx="512" cy="229" r="116" fill="#fff" />
        </g>

        <g filter={`url(#${innerLift})`}>
          <rect x="243" y="408" width="538" height="426" rx="113" fill={`url(#${blue})`} />
          {/* Arms use the body gradient at rest (seamless), swapped to a solid
              hinge colour only while raised — see the component doc comment. */}
          <rect className="mascot-arm-l-b" x="188" y="514" width="90" height="171" rx="19" fill={`url(#${blue})`} />
          <rect className="mascot-arm-r-b" x="746" y="514" width="90" height="171" rx="19" fill={`url(#${blue})`} />
          <rect x="479" y="337" width="66" height="119" rx="6" fill={`url(#${blue})`} />
        </g>

        <rect x="295" y="464" width="434" height="315" rx="78" fill="#fff" />
        <circle className="mascot-glow" cx="512" cy="229" r="96" fill={glowFill} />
        <circle className="mascot-light" cx="512" cy="229" r="73" fill={lightFill} />

        <Faces mark={`url(#${blueMark})`} accent={`url(#${yellow})`} />

        {/* Prop: mini terminal, propped in front of the lower torso while a turn
            runs. Dark lid with a white stroke ring (a white frame melts into the
            white face screen behind it); the slight tilt sells "object set down
            in front", and the base bar rests on the composer rim. The arms do
            not grip it — tokens.css only tucks them inward (see doc comment). */}
        <g className="mascot-prop-laptop" transform="rotate(-2.5 512 800)">
          <g filter={`url(#${softShadow})`}>
            <rect x="338" y="698" width="348" height="164" rx="16" fill="#1d2433" stroke="#fff" strokeWidth="20" />
            <rect x="300" y="862" width="424" height="26" rx="13" fill="#fff" />
          </g>
          <g clipPath={`url(#${laptopClip})`}>
            <g className="mascot-laptop-lines" strokeLinecap="round" strokeWidth="16" fill="none">
              <path d="M382 744h118" stroke="#8ca2ff" />
              <path d="M382 780h170" stroke="#5fd3a6" />
              <path d="M382 816h84" stroke="#8ca2ff" opacity="0.75" />
              <path className="mascot-laptop-caret" d="M478 816h30" stroke="#ffcf11" />
              <path d="M382 852h140" stroke="#5fd3a6" opacity="0.8" />
              <path d="M382 888h96" stroke="#8ca2ff" />
            </g>
          </g>
        </g>

        {/* Prop: question sign, anchored to the landed hand tip of the raised
            right arm (translate(-48,88) rotate(-128°) scaleY(0.8) → tip ≈878,497;
            the pole overlaps the hand = gripped). Revealed only after the arm
            lands — tokens.css sequences arm-first-in / sign-first-out. */}
        <g className="mascot-prop-sign" filter={`url(#${softShadow})`}>
          <rect x="866" y="356" width="24" height="150" rx="12" fill="#fff" />
          <rect x="758" y="190" width="240" height="170" rx="26" fill="#fff" />
          <path d="M843 247a37 37 0 0 1 70 12c0 24-36 36-36 36" stroke="#3161f7" strokeWidth="26" fill="none" strokeLinecap="round" />
          <circle cx="878" cy="343" r="15" fill="#3161f7" />
        </g>
      </g>
    </svg>
  )
}

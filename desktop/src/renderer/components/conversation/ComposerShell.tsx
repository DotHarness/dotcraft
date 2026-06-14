import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useRef,
  useState,
  type AnimationEvent,
  type ButtonHTMLAttributes,
  type CSSProperties,
  type DragEventHandler,
  type JSX,
  type ReactNode
} from 'react'
import { Bot, ListChecks, Square, X } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import { MascotRobot, type MascotExpression, type MascotLight } from './MascotRobot'
import type { AvatarSpec } from '../agents/agentAvatar'
import { MascotBubble, type MascotBubbleAction, type MascotBubbleTone } from './MascotBubble'
import { consumeMascotHandoff, recordMascotHandoff } from './mascotHandoff'
import { ContextMenu, type ContextMenuItem, type ContextMenuPosition } from '../ui/ContextMenu'
import type { ShortcutSpec } from '../ui/shortcutKeys'

/** Bubble content shown above the mascot (copy already localized by the caller). */
export interface ComposerMascotBubble {
  tone?: MascotBubbleTone
  title: string
  body?: string
  actions?: MascotBubbleAction[]
}

/**
 * State-driven mascot behavior supplied by the in-conversation composer.
 * When omitted (e.g. the welcome composer) the mascot keeps its ambient
 * focus/drag-driven expression and no bubble or right-click menu.
 */
export interface ComposerMascotInteraction {
  /** Overrides the ambient focus/drag expression when set. */
  expression?: MascotExpression
  /** Antenna status light (semantic). */
  light?: MascotLight
  /** Non-blocking bubble above the mascot; null/undefined hides it. Dismissal is
   *  one of the bubble's own reply actions (no separate close control). */
  bubble?: ComposerMascotBubble | null
  /** Right-click preset actions (already localized). Empty disables the menu. */
  menuItems?: ContextMenuItem[]
  /** Held prop pose: 'sign' raises the right arm (wave hinge grammar) with the
   *  "?" sign — used by the approval composer. Suppresses the laptop prop. */
  hold?: 'sign'
}

/**
 * Shared mascot pose for the bottom-dock decision composers (tool approval,
 * plan approval, ask-question). All three are "awaiting your decision" UIs in
 * the same dock slot, so they use one pose: the operator face with the raised
 * "?" sign. The held sign also suppresses the mini-terminal (laptop) prop, which
 * would otherwise wrongly imply a running turn.
 */
export const DECISION_MASCOT: ComposerMascotInteraction = { expression: 'operator', hold: 'sign' }

type ComposerActionButtonTone = 'enabled' | 'disabled'

export const COMPOSER_FOOTER_CONTROL_HEIGHT = 24
export const composerFooterControlHoverBackground = 'var(--sidebar-control-hover)'
export const composerFooterControlActiveBackground = 'var(--sidebar-control-active)'

export const composerFooterControlBoxStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  height: COMPOSER_FOOTER_CONTROL_HEIGHT
}

interface ComposerShellProps {
  dragOver: boolean
  dropLabel: string
  topAccessory?: ReactNode
  topAccessoryVisible?: boolean
  attachmentStrip?: ReactNode
  editor: ReactNode
  footerLeading: ReactNode
  footerAction: ReactNode
  belowFooter?: ReactNode
  onDragOver: DragEventHandler<HTMLDivElement>
  onDragLeave: DragEventHandler<HTMLDivElement>
  onDrop: DragEventHandler<HTMLDivElement>
  opacity?: number
  focused?: boolean
  /** Show the DotCraft mascot standing on the composer's top-right edge. */
  showMascot?: boolean
  /** Monotonic counter; bump on send to trigger the one-shot launch jump. */
  mascotBounceSignal?: number
  /** State-driven expression/light/bubble/right-click menu for the mascot. */
  mascotInteraction?: ComposerMascotInteraction
  /** Optional Agent Profile character: recolors the mascot to the profile's palette. */
  mascotAvatar?: AvatarSpec
  /** Participate in cross-composer position handoff: when this shell replaces
   *  (or is replaced by) another handoff shell — input ↔ approval — the mascot
   *  rides between the two rims instead of hard-cutting. */
  mascotHandoff?: boolean
}

const MASCOT_SIZE = 58
/** Default display scale; applied via a wrapper so it shrinks the motion too. */
const MASCOT_SCALE = 0.75
/** Fraction of the mascot tucked behind the composer rim (only its feet rest on the edge). */
const MASCOT_HIDDEN_RATIO = 0.06
/** Extra upward nudge so the (scaled) feet sit flush on the rim, not sunk or floating. */
const MASCOT_RAISE = 3
/** Ambient idle time before the mascot dozes off (woken by any interaction). */
const MASCOT_SLEEP_AFTER_MS = 90_000

type MascotMicro = 'blink' | 'look-l' | 'look-r' | 'bob'

/** Deterministic star-burst offsets for the success celebration. */
const MASCOT_SPARKLES = Array.from({ length: 7 }, (_, i) => {
  const angle = ((-150 + i * 40) * Math.PI) / 180
  const radius = 26 + (i % 3) * 9
  return {
    dx: `${(Math.cos(angle) * radius).toFixed(1)}px`,
    dy: `${(Math.sin(angle) * radius - 8).toFixed(1)}px`,
    delay: `${i * 40}ms`
  }
})

function prefersReducedMotion(): boolean {
  // matchMedia is always present in Electron; guard for the jsdom test env.
  return typeof window.matchMedia === 'function'
    ? window.matchMedia('(prefers-reduced-motion: reduce)').matches
    : false
}

/**
 * DotCraft mascot standing on the composer's top-right edge.
 *
 * Nested transform layers keep the animations from clobbering each other's
 * `transform`: display scale → pose (focus perk-up / error droop / sleep
 * slump) → one-shot (launch/cheer/shake/startle/nod) → loop (breathe / think
 * sway / eager hop / sleep breathe) → hover jelly → SVG. Sub-part animations
 * (arms, antenna, eyes) live inside the SVG via the mascot-* class hooks.
 *
 * Behavior on top of the conversation-driven expression/light:
 * - idle micro-behaviors: random blink / glance / antenna bob;
 * - turn running (operator face, default light): sway + antenna light pulse,
 *   and after 1.2s the mini-terminal prop fades in (arms tuck inward);
 * - success light: raised-arm cheer with a green flash and star burst;
 * - error light: head shake, then a deflated droop while the light stays red;
 * - drag-over: eager hop with arms spread;
 * - ambient idle for a while: dozes off (Zzz, dim light), startled awake;
 * - interaction.hold === 'sign': right arm raises (wave hinge) holding the
 *   "?" sign — the approval composer's pose;
 * - handoff: rides between composer rims across the input ↔ approval remount;
 * - click: a flipper wave (easter egg). All gated by prefers-reduced-motion.
 */
function ComposerMascot({
  focused,
  dragOver,
  bounceSignal,
  interaction,
  avatar,
  handoff = false
}: {
  focused: boolean
  dragOver: boolean
  bounceSignal: number
  interaction?: ComposerMascotInteraction
  avatar?: AvatarSpec
  handoff?: boolean
}): JSX.Element {
  const [menuPos, setMenuPos] = useState<ContextMenuPosition | null>(null)
  const [micro, setMicro] = useState<MascotMicro | null>(null)
  const [sleeping, setSleeping] = useState(false)
  const [waving, setWaving] = useState(false)
  const [startled, setStartled] = useState(false)
  const [launching, setLaunching] = useState(false)
  const [cheering, setCheering] = useState(false)
  const [sparkling, setSparkling] = useState(false)
  const [shaking, setShaking] = useState(false)
  const [nodding, setNodding] = useState(false)
  const [landing, setLanding] = useState(false)
  const rootRef = useRef<HTMLDivElement | null>(null)

  // Conversation state overrides the ambient focus/drag expression when present.
  const baseExpression: MascotExpression =
    interaction?.expression ?? (dragOver ? 'operator' : focused ? 'happy' : 'neutral')
  const light: MascotLight = interaction?.light ?? 'default'
  const menuItems = interaction?.menuItems ?? []
  const bubble = interaction?.bubble ?? null
  const holdSign = interaction?.hold === 'sign'
  // Mini terminal: same condition as the think loop (a turn is running) minus
  // held props and bubble overrides (an operator face with a bubble is a local
  // confirm/busy state, not a running turn); the 1.2s reveal delay lives in
  // tokens.css so quick turns never flash it.
  const laptopActive =
    !sleeping &&
    !dragOver &&
    !holdSign &&
    bubble == null &&
    baseExpression === 'operator' &&
    light === 'default'
  // Local behaviors (sleep, wave) override the face; conversation light stays.
  const expression: MascotExpression = sleeping ? 'sleep' : waving ? 'happy' : baseExpression

  // Cross-composer ride: the approval composer replaces the input composer (a
  // full remount), so the outgoing mascot records its screen position in the
  // layout cleanup (node still attached) and the incoming one starts from that
  // offset, riding to its own rim — spring up with a startle, or an
  // accelerated drop punctuated by the landing squash.
  useLayoutEffect(() => {
    if (!handoff) return undefined
    const el = rootRef.current
    if (!el) return undefined
    let timer = 0
    const dy = prefersReducedMotion() ? null : consumeMascotHandoff(el)
    if (dy != null) {
      const rising = dy > 0
      el.style.transition = 'none'
      el.style.transform = `translateY(${dy}px)`
      void el.offsetHeight
      el.style.transition = rising
        ? 'transform 420ms cubic-bezier(0.34, 1.56, 0.64, 1)'
        : 'transform 300ms cubic-bezier(0.55, 0, 0.8, 0.9)'
      el.style.transform = 'translateY(0)'
      if (rising) setStartled(true)
      timer = window.setTimeout(() => {
        el.style.transition = ''
        el.style.transform = ''
        if (!rising) setLanding(true)
      }, rising ? 430 : 310)
    }
    return () => {
      window.clearTimeout(timer)
      recordMascotHandoff(el)
    }
  }, [handoff])

  // Replay the send launch via state (not a remount) so other one-shots can
  // share the same transform layer without re-triggering it.
  const prevBounceRef = useRef(bounceSignal)
  useEffect(() => {
    if (bounceSignal === prevBounceRef.current) return
    prevBounceRef.current = bounceSignal
    if (!prefersReducedMotion()) setLaunching(true)
  }, [bounceSignal])

  // Celebrate / deflate on live light transitions (not on mount, so loading a
  // finished thread does not replay the celebration).
  const prevLightRef = useRef(light)
  useEffect(() => {
    const prev = prevLightRef.current
    prevLightRef.current = light
    if (light === prev || prefersReducedMotion()) return
    if (light === 'success') {
      setCheering(true)
      setSparkling(true)
    } else if (light === 'error') {
      setShaking(true)
    }
  }, [light])

  // Doze off after a long ambient idle; any state change wakes the mascot.
  useEffect(() => {
    const ambient =
      !focused && !dragOver && !bubble && baseExpression === 'neutral' && light === 'default'
    if (!ambient || prefersReducedMotion()) {
      setSleeping(false)
      return
    }
    if (sleeping) return
    const timer = window.setTimeout(() => setSleeping(true), MASCOT_SLEEP_AFTER_MS)
    return () => window.clearTimeout(timer)
  }, [focused, dragOver, bubble, baseExpression, light, sleeping])

  const wake = useCallback(() => {
    setSleeping(false)
    if (!prefersReducedMotion()) setStartled(true)
  }, [])

  // Idle micro-behaviors: occasional blink / glance / antenna bob.
  useEffect(() => {
    if (sleeping || prefersReducedMotion()) return
    if (baseExpression !== 'neutral' && baseExpression !== 'happy') return
    let cancelled = false
    let timer = 0
    const schedule = (): void => {
      timer = window.setTimeout(
        () => {
          if (cancelled) return
          if (!document.hidden) {
            const r = Math.random()
            if (r < 0.5) setMicro('blink')
            else if (r < 0.72) setMicro(Math.random() < 0.5 ? 'look-l' : 'look-r')
            else if (r < 0.86) setMicro('bob')
          }
          schedule()
        },
        2600 + Math.random() * 3200
      )
    }
    schedule()
    return () => {
      cancelled = true
      window.clearTimeout(timer)
    }
  }, [sleeping, baseExpression])

  // Typing nod: keystrokes land here only while the composer editor is focused;
  // the animation's own duration throttles the cadence.
  useEffect(() => {
    if (!focused || prefersReducedMotion()) return
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.ctrlKey || event.metaKey || event.altKey) return
      setNodding(true)
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [focused])

  // One-shot states clear when their animation finishes (events bubble up here).
  const onAnimationEnd = (event: AnimationEvent<HTMLDivElement>): void => {
    switch (event.animationName) {
      case 'composer-mascot-blink':
        // Neutral's caret flash runs longer than the eye squash; let it finish.
        if (expression !== 'neutral') setMicro(null)
        break
      case 'composer-mascot-caret':
      case 'composer-mascot-look-l':
      case 'composer-mascot-look-r':
      case 'composer-mascot-antenna-bob':
        setMicro(null)
        break
      case 'composer-mascot-launch':
        setLaunching(false)
        break
      case 'composer-mascot-cheer':
        setCheering(false)
        break
      case 'composer-mascot-sparkle':
        setSparkling(false)
        break
      case 'composer-mascot-shake':
        setShaking(false)
        break
      case 'composer-mascot-startle':
        setStartled(false)
        break
      case 'composer-mascot-wave-arm':
        setWaving(false)
        break
      case 'composer-mascot-nod':
        setNodding(false)
        break
      case 'composer-mascot-land':
        setLanding(false)
        break
    }
  }

  // Pose layer: sleep slump > error droop > focus perk-up.
  const poseTransform = sleeping
    ? 'translateY(2px) rotate(2.6deg) scale(0.985)'
    : light === 'error'
      ? 'translateY(2px) rotate(-3deg) scale(0.98)'
      : focused
        ? 'scale(1.1)'
        : 'scale(1)'

  // One transform slot for one-shots; priority resolves rare overlaps.
  const shotClass = cheering
    ? 'composer-mascot-cheer'
    : shaking
      ? 'composer-mascot-shake'
      : startled
        ? 'composer-mascot-startle'
        : landing
          ? 'composer-mascot-land'
          : launching
            ? 'composer-mascot-launch'
            : nodding
              ? 'composer-mascot-nod'
              : undefined

  const loopClass = sleeping
    ? 'composer-mascot-sleep-breathe'
    : dragOver
      ? 'composer-mascot-eager'
      : baseExpression === 'operator' && light === 'default'
        ? 'composer-mascot-think'
        : 'composer-mascot-breathe'

  const rootClassName =
    [
      micro ? `composer-mascot-${micro}` : null,
      waving ? 'composer-mascot-wave' : null,
      sleeping ? 'composer-mascot-sleeping' : null,
      light === 'success' ? 'composer-mascot-celebrate' : null,
      light === 'error' ? 'composer-mascot-deflate' : null,
      holdSign ? 'composer-mascot-hold-sign' : null,
      laptopActive ? 'composer-mascot-prop-laptop' : null
    ]
      .filter(Boolean)
      .join(' ') || undefined

  return (
    <div
      // Decorative only until it carries a bubble or a right-click menu.
      aria-hidden={interaction ? undefined : true}
      ref={rootRef}
      className={rootClassName}
      onAnimationEnd={onAnimationEnd}
      style={{
        position: 'absolute',
        right: '40px',
        top: `${-(MASCOT_SIZE * (1 - MASCOT_HIDDEN_RATIO)) - MASCOT_RAISE}px`,
        zIndex: 0,
        pointerEvents: 'none'
      }}
    >
      {bubble && (
        <div
          style={{
            position: 'absolute',
            right: 0,
            bottom: 'calc(100% + 8px)',
            zIndex: 5,
            pointerEvents: 'auto'
          }}
        >
          <MascotBubble
            tone={bubble.tone}
            title={bubble.title}
            body={bubble.body}
            actions={bubble.actions}
          />
        </div>
      )}

      {/* Display scale: shrinks size + all nested motion uniformly, feet planted.
          Also the prefers-reduced-motion scope (see tokens.css). */}
      <div
        className="composer-mascot-motion"
        style={{
          transformOrigin: 'bottom center',
          transform: `scale(${MASCOT_SCALE})`,
          // Mascot drop-shadow biases downward so it reads with the contact shadow on
          // the rim below. Raw navy is a brand-asset rendering artifact (mirrors the
          // robot's own shadows in MascotRobot), not a themed surface color.
          filter: 'drop-shadow(0 5.3px 7.3px color-mix(in srgb, #0b3d62 20%, transparent))'
        }}
      >
        {/* Pose layer: focus perk-up / error droop / sleep slump (feet planted). */}
        <div
          style={{
            transformOrigin: 'bottom center',
            transition: 'transform 280ms cubic-bezier(0.34, 1.56, 0.64, 1)',
            transform: poseTransform
          }}
        >
          {/* One-shot layer: launch / cheer / shake / startle / nod. */}
          <div className={shotClass}>
            {/* Activity loop: breathe / think sway / eager hop / sleep breathe. */}
            <div className={loopClass}>
              {/* Hover jelly: pointer-events re-enabled here so only the visible
                  robot (above the rim) is hoverable; the rest stays click-through. */}
              <div
                className="composer-mascot-jelly"
                style={{ pointerEvents: 'auto', cursor: menuItems.length > 0 ? 'context-menu' : undefined }}
                onMouseEnter={sleeping ? wake : undefined}
                onClick={() => {
                  if (sleeping) {
                    wake()
                    return
                  }
                  if (!prefersReducedMotion()) setWaving(true)
                }}
                onContextMenu={
                  menuItems.length > 0
                    ? (e) => {
                        e.preventDefault()
                        setMenuPos({ x: e.clientX, y: e.clientY })
                      }
                    : undefined
                }
              >
                <MascotRobot expression={expression} light={light} size={MASCOT_SIZE} avatar={avatar} />
              </div>
            </div>
          </div>
        </div>

        {sleeping && (
          <div aria-hidden className="composer-mascot-zzz">
            <span>z</span>
            <span>z</span>
            <span>z</span>
          </div>
        )}
        {sparkling && (
          <div aria-hidden className="composer-mascot-sparkles">
            {MASCOT_SPARKLES.map((s, i) => (
              <i
                key={i}
                style={{ '--dx': s.dx, '--dy': s.dy, animationDelay: s.delay } as CSSProperties}
              />
            ))}
          </div>
        )}
      </div>

      {menuPos && menuItems.length > 0 && (
        <ContextMenu items={menuItems} position={menuPos} onClose={() => setMenuPos(null)} />
      )}
    </div>
  )
}

interface ComposerPlanModeLabelProps {
  value: 'agent' | 'plan'
  onDisable: () => void
  label: string
  title: string
  ariaLabel: string
  shortcut?: ShortcutSpec
}

export function ComposerShell({
  dragOver,
  dropLabel,
  topAccessory,
  topAccessoryVisible = false,
  attachmentStrip,
  editor,
  footerLeading,
  footerAction,
  belowFooter,
  onDragOver,
  onDragLeave,
  onDrop,
  opacity = 1,
  focused = false,
  showMascot = false,
  mascotBounceSignal = 0,
  mascotInteraction,
  mascotAvatar,
  mascotHandoff = false
}: ComposerShellProps): JSX.Element {
  const [hovered, setHovered] = useState(false)
  return (
    <div
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        position: 'relative',
        padding: '0 0 14px',
        display: 'flex',
        flexDirection: 'column',
        gap: 0,
        opacity,
        isolation: 'isolate'
      }}
    >
      {showMascot && !topAccessoryVisible && (
        <ComposerMascot
          focused={focused}
          dragOver={dragOver}
          bounceSignal={mascotBounceSignal}
          interaction={mascotInteraction}
          avatar={mascotAvatar}
          handoff={mascotHandoff}
        />
      )}
      {topAccessoryVisible && (
        <div
          data-testid="composer-top-accessory-overlay"
          style={{
            position: 'absolute',
            insetInline: 0,
            bottom: 'calc(100% - 1px)',
            zIndex: 0,
            pointerEvents: 'none'
          }}
        >
          {topAccessory}
        </div>
      )}
      {/* Card-only wrapper: scopes the focus glow to the card (not the outer
          container, which also holds the footer) so the halo hugs the card. */}
      <div style={{ position: 'relative' }}>
        {/* Brand-gradient glow behind the composer. Always mounted so it can ease
            in and out on hover instead of popping the moment the pointer crosses
            the edge; transparent at rest, a calmer static halo on hover, and it
            breathes on focus. Sits behind the opaque card so only the rim shows.
            Scoped to this card-only wrapper (NOT the outer container, which also
            holds the Local/branch footer) so the halo hugs the card evenly on
            every side instead of spreading down behind that footer below. */}
        <div
          aria-hidden
          className={focused ? 'composer-focus-glow' : undefined}
          style={{
            position: 'absolute',
            inset: '-3px',
            borderRadius: '23px',
            background: 'var(--composer-focus-glow)',
            filter: 'blur(8px)',
            opacity: focused ? 0.22 : hovered ? 0.18 : 0,
            // Gentle, symmetric fade so the diffuse halo eases in and out rather
            // than snapping. (While focused, the breathing animation drives
            // opacity instead, so this transition only governs the hover halo.)
            transition: 'opacity 420ms ease',
            zIndex: -1,
            pointerEvents: 'none'
          }}
        />
        <div
          style={{
            position: 'relative',
            zIndex: 1,
            // Tokenized rest border keeps light theme legible while dark theme
            // stays effectively frameless. Focus adds a subtle brand-blue rim.
            border: focused
              ? '1px solid var(--composer-focus-border)'
              : '1px solid var(--composer-input-rest-border)',
            borderRadius: '20px',
            background: 'var(--composer-input-background)',
            padding: '10px 10px 8px',
            transition: 'border-color 0.2s ease',
            boxShadow: topAccessoryVisible
              ? 'var(--composer-input-shadow), inset 0 1px 0 var(--composer-top-accessory-separator)'
              : 'var(--composer-input-shadow)'
          }}
          onDragOver={onDragOver}
          onDragLeave={onDragLeave}
          onDrop={onDrop}
        >
          {showMascot && !topAccessoryVisible && (
            // Contact shadow cast by the mascot's feet onto the composer rim, so the
            // robot reads as standing on the surface rather than floating above it.
            // Anchored under the mascot (right:40 + half width 29 − 1px border ≈ 68);
            // translateX(50%) centers the blob on that point. Brand-asset rendering
            // artifact (raw navy mirrors MascotRobot's shadows), not a themed color.
            <div
              aria-hidden
              style={{
                position: 'absolute',
                right: '68px',
                top: '1px',
                width: '72px',
                height: '24px',
                transform: 'translateX(50%)',
                borderRadius: '50%',
                background:
                  'radial-gradient(50% 100% at 50% 0%, color-mix(in srgb, #0b3d62 10%, transparent) 0%, transparent 72%)',
                filter: 'blur(2px)',
                pointerEvents: 'none'
              }}
            />
          )}
          {dragOver && (
            <div
              style={{
                position: 'absolute',
                inset: 0,
                zIndex: 20,
                border: '2px dashed var(--accent)',
                borderRadius: '18px',
                background: 'rgba(124, 58, 237, 0.08)',
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
                pointerEvents: 'none',
                fontSize: 'var(--type-ui-size)',
                lineHeight: 'var(--type-ui-line-height)',
                color: 'var(--accent)'
              }}
            >
              {dropLabel}
            </div>
          )}

          {attachmentStrip}
          {editor}

          <div
            style={{
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'space-between',
              gap: '10px',
              marginTop: '8px',
              paddingTop: '6px'
            }}
          >
            {footerLeading}
            {footerAction}
          </div>
        </div>
      </div>
      {belowFooter && (
        <div
          style={{
            position: 'relative',
            zIndex: 1,
            marginTop: '6px'
          }}
        >
          {belowFooter}
        </div>
      )}
    </div>
  )
}

export function ComposerPlanModeLabel({
  value,
  onDisable,
  label,
  title,
  ariaLabel,
  shortcut
}: ComposerPlanModeLabelProps): JSX.Element | null {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const active = hovered || focused
  const Icon = active ? X : ListChecks

  if (value !== 'plan') return null

  return (
    <ActionTooltip label={title} shortcut={shortcut} placement="top">
      <button
        type="button"
        onClick={onDisable}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        aria-label={ariaLabel}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '6px',
          height: COMPOSER_FOOTER_CONTROL_HEIGHT,
          padding: '0 6px',
          borderRadius: '999px',
          border: 'none',
          background: active ? composerFooterControlHoverBackground : 'transparent',
          color: 'var(--composer-footer-text)',
          cursor: 'pointer',
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)',
          outline: 'none',
          transition: 'background-color 120ms ease, color 120ms ease'
        }}
    >
        <Icon size={13} strokeWidth={2} aria-hidden />
        <span>{label}</span>
      </button>
    </ActionTooltip>
  )
}

interface ComposerCustomProfileLabelProps {
  label: string
  onClear: () => void
  title: string
  ariaLabel: string
}

/**
 * Footer pill shown when the active thread is backed by an agent profile. Replaces the Plan pill
 * (a profile-backed thread has no operational mode). Hover/focus reveals the clear (×) affordance.
 */
export function ComposerCustomProfileLabel({ label, onClear, title, ariaLabel }: ComposerCustomProfileLabelProps): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const active = hovered || focused
  const Icon = active ? X : Bot

  return (
    <ActionTooltip label={title} placement="top">
      <button
        type="button"
        onClick={onClear}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        aria-label={ariaLabel}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '6px',
          height: COMPOSER_FOOTER_CONTROL_HEIGHT,
          padding: '0 8px',
          borderRadius: '999px',
          border: 'none',
          background: active ? composerFooterControlHoverBackground : 'color-mix(in srgb, var(--accent) 12%, transparent)',
          color: 'var(--composer-footer-text)',
          cursor: 'pointer',
          fontSize: 'var(--type-secondary-size)',
          lineHeight: 'var(--type-secondary-line-height)',
          fontWeight: 'var(--type-ui-emphasis-weight)',
          outline: 'none',
          transition: 'background-color 120ms ease, color 120ms ease'
        }}
      >
        <Icon size={13} strokeWidth={2} aria-hidden />
        <span>{label}</span>
      </button>
    </ActionTooltip>
  )
}

export function composerModelPillStyle(color: string, disabled = false): CSSProperties {
  return {
    fontSize: 'var(--type-secondary-size)',
    lineHeight: 'var(--type-secondary-line-height)',
    fontWeight: 'var(--type-ui-emphasis-weight)',
    color,
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    maxWidth: '220px',
    height: COMPOSER_FOOTER_CONTROL_HEIGHT,
    borderRadius: '999px',
    border: 'none',
    backgroundColor: 'transparent',
    padding: '0 4px',
    outline: 'none',
    whiteSpace: 'nowrap',
    overflow: 'hidden',
    textOverflow: 'ellipsis',
    opacity: disabled ? 0.72 : 1,
    boxShadow: 'none',
    transition: 'background-color 120ms ease, color 120ms ease'
  }
}

export const composerActionButtonStyle: CSSProperties = {
  width: '32px',
  height: '32px',
  borderRadius: '999px',
  border: 'none',
  flexShrink: 0,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  cursor: 'pointer',
  boxShadow: 'var(--composer-action-shadow)',
  transition: 'background-color 100ms ease, transform 100ms ease'
}

export function composerSendButtonStyle(tone: ComposerActionButtonTone, active = false): CSSProperties {
  const enabled = tone === 'enabled'

  return {
    ...composerActionButtonStyle,
    backgroundColor: enabled
      ? active
        ? '#ffffff'
        : '#f5f6f7'
      : 'color-mix(in srgb, var(--bg-primary) 92%, #ffffff 8%)',
    color: enabled ? '#1f2328' : 'var(--text-dimmed)',
    cursor: enabled ? 'pointer' : 'default',
    transform: enabled && active ? 'translateY(-1px)' : 'none'
  }
}

interface ComposerSendButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  tone: ComposerActionButtonTone
}

export function ComposerSendButton({
  tone,
  children,
  onMouseEnter,
  onMouseLeave,
  onFocus,
  onBlur,
  ...props
}: ComposerSendButtonProps): JSX.Element {
  const [active, setActive] = useState(false)
  const enabled = tone === 'enabled' && !props.disabled

  return (
    <button
      {...props}
      type={props.type ?? 'button'}
      onMouseEnter={(event) => {
        if (enabled) setActive(true)
        onMouseEnter?.(event)
      }}
      onMouseLeave={(event) => {
        setActive(false)
        onMouseLeave?.(event)
      }}
      onFocus={(event) => {
        if (enabled && event.currentTarget.matches(':focus-visible')) setActive(true)
        onFocus?.(event)
      }}
      onBlur={(event) => {
        setActive(false)
        onBlur?.(event)
      }}
      style={{
        ...composerSendButtonStyle(tone, active),
        ...props.style
      }}
    >
      {children}
    </button>
  )
}

export function SendIcon(): JSX.Element {
  return (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
      <path d="M12 19a1.25 1.25 0 0 1-1.25-1.25v-8.03l-3.1 3.1a1.25 1.25 0 1 1-1.77-1.77l5.24-5.24a1.25 1.25 0 0 1 1.76 0l5.24 5.24a1.25 1.25 0 1 1-1.77 1.77l-3.1-3.1v8.03A1.25 1.25 0 0 1 12 19Z" />
    </svg>
  )
}

export function StopIcon(): JSX.Element {
  return <Square size={12} strokeWidth={0} fill="currentColor" aria-hidden="true" />
}

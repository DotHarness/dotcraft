import {
  useState,
  type ButtonHTMLAttributes,
  type CSSProperties,
  type DragEventHandler,
  type JSX,
  type ReactNode
} from 'react'
import { ListChecks, Square, X } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import { MascotRobot, type MascotExpression, type MascotLight } from './MascotRobot'
import { MascotBubble, type MascotBubbleAction, type MascotBubbleTone } from './MascotBubble'
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
}

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
  /** Monotonic counter; bump on send to trigger a one-shot bounce. */
  mascotBounceSignal?: number
  /** State-driven expression/light/bubble/right-click menu for the mascot. */
  mascotInteraction?: ComposerMascotInteraction
}

const MASCOT_SIZE = 58
/** Default display scale; applied via a wrapper so it shrinks the motion too. */
const MASCOT_SCALE = 0.75
/** Fraction of the mascot tucked behind the composer rim (only its feet rest on the edge). */
const MASCOT_HIDDEN_RATIO = 0.06
/** Extra upward nudge so the (scaled) feet sit flush on the rim, not sunk or floating. */
const MASCOT_RAISE = 3

/**
 * DotCraft mascot standing on the composer's top-right edge.
 * Nested transform layers keep idle breathing, focus perk-up, and the send
 * bounce from clobbering each other's `transform`. The face swaps by state:
 * drag → operator, inputting (focused) → happy, else neutral.
 */
function ComposerMascot({
  focused,
  dragOver,
  bounceSignal,
  interaction
}: {
  focused: boolean
  dragOver: boolean
  bounceSignal: number
  interaction?: ComposerMascotInteraction
}): JSX.Element {
  const [menuPos, setMenuPos] = useState<ContextMenuPosition | null>(null)
  // Conversation state overrides the ambient focus/drag expression when present.
  const expression: MascotExpression =
    interaction?.expression ?? (dragOver ? 'operator' : focused ? 'happy' : 'neutral')
  const light: MascotLight = interaction?.light ?? 'default'
  const menuItems = interaction?.menuItems ?? []
  const bubble = interaction?.bubble ?? null

  return (
    <div
      // Decorative only until it carries a bubble or a right-click menu.
      aria-hidden={interaction ? undefined : true}
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

      {/* Display scale: shrinks size + all nested motion uniformly, feet planted. */}
      <div
        style={{
          transformOrigin: 'bottom center',
          transform: `scale(${MASCOT_SCALE})`,
          // Mascot drop-shadow biases downward so it reads with the contact shadow on
          // the rim below. Raw navy is a brand-asset rendering artifact (mirrors the
          // robot's own shadows in MascotRobot), not a themed surface color.
          filter: 'drop-shadow(0 5.3px 7.3px color-mix(in srgb, #0b3d62 20%, transparent))'
        }}
      >
        {/* Focus perk-up: grow in place (feet stay planted on the edge) when focused. */}
        <div
          style={{
            transformOrigin: 'bottom center',
            transition: 'transform 280ms cubic-bezier(0.34, 1.56, 0.64, 1)',
            transform: focused ? 'scale(1.1)' : 'scale(1)'
          }}
        >
          {/* Send bounce: remounted by key so the one-shot replays each send. */}
          <div key={bounceSignal} className={bounceSignal > 0 ? 'composer-mascot-bounce' : undefined}>
            {/* Idle breathing. */}
            <div className="composer-mascot-breathe">
              {/* Hover jelly: pointer-events re-enabled here so only the visible
                  robot (above the rim) is hoverable; the rest stays click-through. */}
              <div
                className="composer-mascot-jelly"
                style={{ pointerEvents: 'auto', cursor: menuItems.length > 0 ? 'context-menu' : undefined }}
                onContextMenu={
                  menuItems.length > 0
                    ? (e) => {
                        e.preventDefault()
                        setMenuPos({ x: e.clientX, y: e.clientY })
                      }
                    : undefined
                }
              >
                <MascotRobot expression={expression} light={light} size={MASCOT_SIZE} />
              </div>
            </div>
          </div>
        </div>
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
  mascotInteraction
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
        {(focused || hovered) && (
          // Brand-gradient glow behind the composer. Breathes on focus; a calmer
          // static halo on hover. Sits behind the opaque card so only the rim
          // shows. Scoped to this card-only wrapper (NOT the outer container, which
          // also holds the Local/branch footer) so the halo hugs the card evenly on
          // every side instead of spreading down behind that footer below.
          <div
            aria-hidden
            className={focused ? 'composer-focus-glow' : undefined}
            style={{
              position: 'absolute',
              inset: '-3px',
              borderRadius: '23px',
              background: 'var(--composer-focus-glow)',
              filter: 'blur(8px)',
              opacity: focused ? 0.22 : 0.18,
              zIndex: -1,
              pointerEvents: 'none'
            }}
          />
        )}
        <div
          style={{
            position: 'relative',
            zIndex: 1,
            // Frameless at rest: raised fill + soft shadow separate it from the
            // conversation. Focus adds a subtle brand-blue rim over the breathing
            // glow behind. No position or shadow change on hover/focus.
            border: focused
              ? '1px solid var(--composer-focus-border)'
              : '1px solid transparent',
            borderRadius: '20px',
            background: 'var(--composer-input-background)',
            padding: '10px 10px 8px',
            transition: 'border-color 0.2s ease',
            boxShadow: 'var(--composer-input-shadow)'
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

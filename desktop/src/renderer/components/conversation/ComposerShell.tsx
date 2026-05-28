import { useState, type CSSProperties, type DragEventHandler, type JSX, type ReactNode } from 'react'
import { ListChecks, Square, X } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import { MascotRobot, type MascotExpression } from './MascotRobot'
import type { ShortcutSpec } from '../ui/shortcutKeys'

type ComposerActionButtonTone = 'enabled' | 'disabled'

export const COMPOSER_FOOTER_CONTROL_HEIGHT = 24

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
  onDragOver: DragEventHandler<HTMLDivElement>
  onDragLeave: DragEventHandler<HTMLDivElement>
  onDrop: DragEventHandler<HTMLDivElement>
  opacity?: number
  focused?: boolean
  /** Show the DotCraft mascot standing on the composer's top-right edge. */
  showMascot?: boolean
  /** Monotonic counter; bump on send to trigger a one-shot bounce. */
  mascotBounceSignal?: number
}

const MASCOT_SIZE = 58
/** Default display scale; applied via a wrapper so it shrinks the motion too. */
const MASCOT_SCALE = 0.75
/** Fraction of the mascot tucked behind the composer rim (only its feet rest on the edge). */
const MASCOT_HIDDEN_RATIO = 0.06

/**
 * DotCraft mascot standing on the composer's top-right edge.
 * Nested transform layers keep idle breathing, focus perk-up, and the send
 * bounce from clobbering each other's `transform`. The face swaps by state:
 * drag → operator, inputting (focused) → happy, else neutral.
 */
function ComposerMascot({
  focused,
  dragOver,
  bounceSignal
}: {
  focused: boolean
  dragOver: boolean
  bounceSignal: number
}): JSX.Element {
  const expression: MascotExpression = dragOver ? 'operator' : focused ? 'happy' : 'neutral'

  return (
    <div
      aria-hidden
      style={{
        position: 'absolute',
        right: '40px',
        top: `${-(MASCOT_SIZE * (1 - MASCOT_HIDDEN_RATIO))}px`,
        zIndex: 0,
        pointerEvents: 'none'
      }}
    >
      {/* Display scale: shrinks size + all nested motion uniformly, feet planted. */}
      <div
        style={{
          transformOrigin: 'bottom center',
          transform: `scale(${MASCOT_SCALE})`,
          filter: 'drop-shadow(0 4px 8px color-mix(in srgb, var(--accent) 22%, transparent))'
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
              <MascotRobot expression={expression} size={MASCOT_SIZE} />
            </div>
          </div>
        </div>
      </div>
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
  onDragOver,
  onDragLeave,
  onDrop,
  opacity = 1,
  focused = false,
  showMascot = false,
  mascotBounceSignal = 0
}: ComposerShellProps): JSX.Element {
  return (
    <div
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
        <ComposerMascot focused={focused} dragOver={dragOver} bounceSignal={mascotBounceSignal} />
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
      <div
        style={{
          position: 'relative',
          zIndex: 1,
          border: focused
            ? '1px solid var(--composer-input-border-focus)'
            : '1px solid var(--composer-input-border)',
          borderRadius: '20px',
          background: 'var(--composer-input-background)',
          padding: '10px 10px 8px',
          boxShadow: focused
            ? '0 0 0 1px color-mix(in srgb, var(--accent) 16%, transparent), var(--composer-input-shadow)'
            : 'var(--composer-input-shadow)'
        }}
        onDragOver={onDragOver}
        onDragLeave={onDragLeave}
        onDrop={onDrop}
      >
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
          background: active ? 'var(--bg-tertiary)' : 'transparent',
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
    boxShadow: 'none'
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

export function composerSendButtonStyle(tone: ComposerActionButtonTone): CSSProperties {
  const enabled = tone === 'enabled'

  return {
    ...composerActionButtonStyle,
    backgroundColor: enabled ? '#f5f6f7' : 'color-mix(in srgb, var(--bg-primary) 92%, #ffffff 8%)',
    color: enabled ? '#1f2328' : 'var(--text-dimmed)',
    cursor: enabled ? 'pointer' : 'default'
  }
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

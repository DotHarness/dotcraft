import { useState } from 'react'
import type { CSSProperties } from 'react'
import { Target, X } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import {
  COMPOSER_FOOTER_CONTROL_HEIGHT,
  composerFooterControlHoverBackground
} from './ComposerShell'

/**
 * Footer indicator shown only while the composer is in goal-compose mode, which is
 * entered from the `/` system menu — same show/hide logic as the Plan and Custom
 * pills. Clicking it exits the mode; the icon swaps to an X on hover/focus to signal
 * that, exactly like the Plan pill.
 */
export function GoalComposePill({
  label,
  title,
  ariaLabel,
  onExit
}: {
  label: string
  title: string
  ariaLabel: string
  onExit: () => void
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const active = hovered || focused
  const Icon = active ? X : Target

  return (
    <ActionTooltip label={title} placement="top">
      <button
        type="button"
        onClick={onExit}
        onMouseEnter={() => setHovered(true)}
        onMouseLeave={() => setHovered(false)}
        onFocus={() => setFocused(true)}
        onBlur={() => setFocused(false)}
        aria-label={ariaLabel}
        style={goalComposePillStyle(active)}
      >
        <Icon size={13} strokeWidth={2} aria-hidden />
        <span>{label}</span>
      </button>
    </ActionTooltip>
  )
}

function goalComposePillStyle(active: boolean): CSSProperties {
  return {
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
  }
}

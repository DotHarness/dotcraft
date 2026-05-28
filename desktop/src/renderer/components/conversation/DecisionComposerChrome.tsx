import type { CSSProperties, JSX } from 'react'
import { CornerDownLeft } from 'lucide-react'
import { ActionTooltip } from '../ui/ActionTooltip'
import type { ShortcutSpec } from '../ui/shortcutKeys'

interface DecisionDismissButtonProps {
  label: string
  onClick: () => void
  ariaLabel?: string
  tooltipLabel?: string
  shortcut?: ShortcutSpec
  disabled?: boolean
}

interface DecisionSubmitButtonProps {
  label: string
  onClick: () => void
  disabled?: boolean
}

export const decisionComposerBodyStyle: CSSProperties = {
  display: 'grid',
  gap: '8px'
}

export const decisionComposerChoiceListStyle: CSSProperties = {
  display: 'grid',
  gap: '4px'
}

export const decisionComposerTitleRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'flex-start',
  justifyContent: 'space-between',
  gap: '12px',
  color: 'var(--text-primary)'
}

export const decisionComposerTitleStyle: CSSProperties = {
  minWidth: 0,
  flex: '1 1 auto',
  color: 'var(--text-primary)',
  fontSize: '14px',
  fontWeight: 600,
  lineHeight: '20px'
}

export const decisionComposerFooterActionsStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px'
}

export function DecisionDismissButton({
  label,
  onClick,
  ariaLabel,
  tooltipLabel,
  shortcut,
  disabled = false
}: DecisionDismissButtonProps): JSX.Element {
  const button = (
    <button
      type="button"
      onClick={onClick}
      aria-label={ariaLabel ?? label}
      disabled={disabled}
      style={decisionDismissButtonStyle(disabled)}
    >
      <span>{label}</span>
      <span style={decisionKbdChipStyle}>Esc</span>
    </button>
  )

  if (!shortcut) return button

  return (
    <ActionTooltip label={tooltipLabel ?? label} shortcut={shortcut} placement="top">
      {button}
    </ActionTooltip>
  )
}

export function DecisionSubmitButton({
  label,
  onClick,
  disabled = false
}: DecisionSubmitButtonProps): JSX.Element {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      style={decisionSubmitButtonStyle(disabled)}
    >
      <span>{label}</span>
      <CornerDownLeft size={14} strokeWidth={1.9} aria-hidden="true" />
    </button>
  )
}

function decisionDismissButtonStyle(disabled: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    gap: '6px',
    border: 'none',
    background: 'transparent',
    color: 'var(--text-dimmed)',
    fontSize: '12px',
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.6 : 1,
    padding: 0
  }
}

function decisionSubmitButtonStyle(disabled: boolean): CSSProperties {
  return {
    height: '32px',
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    gap: '7px',
    borderRadius: '999px',
    border: '1px solid var(--text-primary)',
    padding: '0 14px',
    background: 'var(--text-primary)',
    color: 'var(--bg-primary)',
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.68 : 1,
    fontSize: '12px',
    fontWeight: 600
  }
}

const decisionKbdChipStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  minWidth: '28px',
  height: '20px',
  padding: '0 6px',
  borderRadius: '4px',
  border: '1px solid var(--border-default)',
  background: 'var(--bg-secondary)',
  color: 'var(--text-secondary)',
  fontSize: '11px',
  fontFamily: 'var(--font-mono, ui-monospace)'
}

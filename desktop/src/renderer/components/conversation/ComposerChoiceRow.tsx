import { useState, type CSSProperties, type KeyboardEvent as ReactKeyboardEvent } from 'react'
import { ArrowDown, ArrowUp, CircleAlert } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
import { SIDEBAR_ROW_MIN_HEIGHT } from '../sidebar/sidebarNavRowStyles'

export type ComposerChoiceDensity = 'compact' | 'decision'

interface ComposerChoiceRowProps {
  index: number
  label: string
  description?: string
  selected: boolean
  canMoveUp: boolean
  canMoveDown: boolean
  onSelect: () => void
  descriptionAriaLabel?: string
  disabled?: boolean
  density?: ComposerChoiceDensity
}

export function ComposerChoiceRow({
  index,
  label,
  description = '',
  selected,
  canMoveUp,
  canMoveDown,
  onSelect,
  descriptionAriaLabel,
  disabled = false,
  density = 'compact'
}: ComposerChoiceRowProps): JSX.Element {
  const t = useT()
  const desc = description.trim()
  const [hovered, setHovered] = useState(false)
  const [focused, setFocused] = useState(false)
  const highlighted = !disabled && (hovered || focused)

  return (
    <div
      role="button"
      tabIndex={disabled ? -1 : 0}
      aria-disabled={disabled}
      aria-pressed={selected}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      onFocus={() => setFocused(true)}
      onBlur={(event) => {
        const nextTarget = event.relatedTarget
        if (!(nextTarget instanceof Node) || !event.currentTarget.contains(nextTarget)) {
          setFocused(false)
        }
      }}
      onClick={() => {
        if (!disabled) onSelect()
      }}
      onKeyDown={(event: ReactKeyboardEvent<HTMLDivElement>) => {
        if (disabled) return
        if (event.key === 'Enter' || event.key === ' ') {
          event.preventDefault()
          onSelect()
        }
      }}
      style={composerChoiceRowStyle(selected, disabled, highlighted, density)}
      aria-label={`${index + 1}. ${label}`}
    >
      <span style={composerChoiceNumberStyle(density)}>{index + 1}.</span>
      <span style={composerChoiceLabelWrapStyle(density)}>
        <span style={composerChoiceLabelStyle(selected, disabled, density)}>{label}</span>
        {desc.length > 0 && (
          <ActionTooltip label={desc} placement="top" multiline>
            <span
              tabIndex={disabled ? -1 : 0}
              role="img"
              aria-label={descriptionAriaLabel ?? t('userInput.optionDescriptionAria', { option: label })}
              style={composerChoiceInfoIconStyle}
              onClick={(event) => event.stopPropagation()}
            >
              <CircleAlert size={14} strokeWidth={1.8} aria-hidden="true" />
            </span>
          </ActionTooltip>
        )}
      </span>
      {selected && <ComposerChoiceArrowHints canMoveUp={canMoveUp} canMoveDown={canMoveDown} />}
    </div>
  )
}

export function ComposerChoiceArrowHints({
  canMoveUp,
  canMoveDown
}: {
  canMoveUp: boolean
  canMoveDown: boolean
}): JSX.Element {
  const t = useT()
  return (
    <span aria-label={`${t('userInput.arrowUpHint')} / ${t('userInput.arrowDownHint')}`} style={composerChoiceArrowHintWrapStyle}>
      <ArrowUp size={14} strokeWidth={1.8} style={composerChoiceArrowIconStyle(canMoveUp)} aria-hidden="true" />
      <ArrowDown size={14} strokeWidth={1.8} style={composerChoiceArrowIconStyle(canMoveDown)} aria-hidden="true" />
    </span>
  )
}

export function composerChoiceRowStyle(
  selected: boolean,
  disabled = false,
  highlighted = false,
  density: ComposerChoiceDensity = 'compact'
): CSSProperties {
  const decision = density === 'decision'

  return {
    display: 'flex',
    alignItems: 'center',
    gap: decision ? '8px' : '7px',
    width: '100%',
    minHeight: decision ? SIDEBAR_ROW_MIN_HEIGHT : '32px',
    padding: decision ? '0 12px' : '4px 8px',
    border: decision ? 'none' : composerChoiceRowBorder(selected, highlighted),
    borderRadius: decision ? 'var(--sidebar-control-radius)' : '8px',
    background: decision
      ? sidebarControlBackground(selected, highlighted)
      : composerChoiceRowBackground(selected, highlighted),
    color: 'var(--text-primary)',
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.72 : 1,
    outline: 'none',
    transition: 'background 100ms ease, border-color 120ms ease, box-shadow 120ms ease'
  }
}

function sidebarControlBackground(selected: boolean, highlighted: boolean): string {
  if (selected) return 'var(--sidebar-control-active)'
  if (highlighted) return 'var(--sidebar-control-hover)'
  return 'transparent'
}

function composerChoiceRowBackground(selected: boolean, highlighted: boolean): string {
  if (selected && highlighted) {
    return 'color-mix(in srgb, var(--bg-tertiary) 76%, var(--text-primary) 14%)'
  }
  if (selected) return 'color-mix(in srgb, var(--bg-tertiary) 82%, var(--text-primary) 8%)'
  if (highlighted) return 'color-mix(in srgb, var(--bg-tertiary) 62%, transparent)'
  return 'transparent'
}

function composerChoiceRowBorder(selected: boolean, highlighted: boolean): string {
  if (selected && highlighted) {
    return '1px solid color-mix(in srgb, var(--text-primary) 14%, transparent)'
  }
  if (selected) {
    return '1px solid color-mix(in srgb, var(--text-primary) 10%, transparent)'
  }
  if (highlighted) {
    return '1px solid color-mix(in srgb, var(--text-primary) 8%, transparent)'
  }
  return '1px solid transparent'
}

export function composerChoiceNumberStyle(density: ComposerChoiceDensity = 'compact'): CSSProperties {
  const decision = density === 'decision'

  return {
    color: 'var(--text-dimmed)',
    width: decision ? '20px' : '22px',
    flex: decision ? '0 0 20px' : '0 0 22px',
    fontSize: decision ? '13px' : 'var(--text-body-size)',
    lineHeight: decision ? '24px' : 'var(--text-body-line-height)',
    fontWeight: 'var(--conversation-font-weight)'
  }
}

function composerChoiceLabelWrapStyle(density: ComposerChoiceDensity): CSSProperties {
  return {
    minWidth: 0,
    flex: '1 1 auto',
    display: 'inline-flex',
    alignItems: 'center',
    gap: density === 'decision' ? '8px' : '7px'
  }
}

export function composerChoiceLabelStyle(
  selected = false,
  disabled = false,
  _density: ComposerChoiceDensity = 'compact'
): CSSProperties {
  // Selection is conveyed by the row background alone; the label keeps the same
  // weight/color in every state so its glyph width and brightness never shift.
  void selected
  return {
    color: disabled ? 'var(--text-dimmed)' : 'var(--text-primary)',
    fontSize: 'var(--text-body-size)',
    fontWeight: 'var(--conversation-font-weight)',
    lineHeight: 'var(--text-body-line-height)',
    minWidth: 0
  }
}

const composerChoiceInfoIconStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  color: 'var(--text-dimmed)',
  cursor: 'help',
  outline: 'none',
  transform: 'translateY(1px)'
}

const composerChoiceArrowHintWrapStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '2px',
  marginLeft: 'auto',
  flexShrink: 0
}

function composerChoiceArrowIconStyle(enabled: boolean): CSSProperties {
  return {
    color: enabled ? 'var(--text-secondary)' : 'var(--text-dimmed)',
    opacity: enabled ? 1 : 0.35
  }
}

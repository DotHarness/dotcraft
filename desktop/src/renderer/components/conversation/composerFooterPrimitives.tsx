import { useState, type CSSProperties, type FocusEvent, type JSX, type ReactNode } from 'react'
import { Check, Search } from 'lucide-react'
import { Input } from '../ui/Input'

const pillStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '6px',
  height: '28px',
  maxWidth: '240px',
  padding: '0 8px',
  border: 'none',
  borderRadius: '999px',
  background: 'transparent',
  color: 'var(--composer-footer-text)',
  font: 'inherit',
  cursor: 'pointer',
  transition: 'background 120ms ease, color 120ms ease, box-shadow 120ms ease'
}

export const menuStyle: CSSProperties = {
  position: 'absolute',
  left: 0,
  bottom: 'calc(100% + 6px)',
  zIndex: 100,
  width: '280px',
  padding: '8px',
  borderRadius: '10px',
  background: 'var(--glass-surface-strong)',
  border: 'none',
  boxShadow: 'var(--glass-shadow-soft)',
  backdropFilter: 'var(--glass-blur)',
  WebkitBackdropFilter: 'var(--glass-blur)',
  color: 'var(--text-primary)'
}

const menuButtonStyle: CSSProperties = {
  width: '100%',
  minHeight: '32px',
  border: 'none',
  borderRadius: '6px',
  background: 'transparent',
  color: 'inherit',
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  padding: '0 8px',
  font: 'inherit',
  cursor: 'pointer',
  textAlign: 'left',
  transition: 'background 120ms ease, color 120ms ease, box-shadow 120ms ease'
}

interface InteractiveState {
  hovered: boolean
  pressed: boolean
  focusVisible: boolean
}

function useInteractiveState(disabled = false): {
  state: InteractiveState
  eventHandlers: {
    onPointerEnter: () => void
    onPointerLeave: () => void
    onPointerDown: () => void
    onPointerUp: () => void
    onPointerCancel: () => void
    onFocus: (event: FocusEvent<HTMLButtonElement>) => void
    onBlur: () => void
  }
} {
  const [state, setState] = useState<InteractiveState>({
    hovered: false,
    pressed: false,
    focusVisible: false
  })

  return {
    state,
    eventHandlers: {
      onPointerEnter: () => {
        if (!disabled) setState((current) => ({ ...current, hovered: true }))
      },
      onPointerLeave: () => {
        setState((current) => ({ ...current, hovered: false, pressed: false }))
      },
      onPointerDown: () => {
        if (!disabled) setState((current) => ({ ...current, pressed: true }))
      },
      onPointerUp: () => {
        setState((current) => ({ ...current, pressed: false }))
      },
      onPointerCancel: () => {
        setState((current) => ({ ...current, pressed: false }))
      },
      onFocus: (event) => {
        if (!disabled && event.currentTarget.matches(':focus-visible')) {
          setState((current) => ({ ...current, focusVisible: true }))
        }
      },
      onBlur: () => {
        setState((current) => ({ ...current, focusVisible: false, pressed: false }))
      }
    }
  }
}

function interactiveStyle(
  state: InteractiveState,
  options: {
    active?: boolean
    disabled?: boolean
  } = {}
): CSSProperties {
  if (options.disabled) {
    return {
      opacity: 0.45,
      cursor: 'default',
      boxShadow: 'none'
    }
  }

  const highlighted = options.active === true || state.hovered || state.focusVisible
  const background = state.pressed
    ? 'var(--bg-active)'
    : highlighted
      ? 'var(--bg-tertiary)'
      : 'transparent'

  return {
    background,
    color: highlighted ? 'var(--text-primary)' : undefined,
    boxShadow: state.focusVisible
      ? '0 0 0 2px color-mix(in srgb, var(--accent) 55%, transparent)'
      : 'none'
  }
}

export function WorkspaceFooterPill({
  children,
  disabled,
  open,
  onClick,
  id,
  'aria-label': ariaLabel,
  'aria-haspopup': ariaHasPopup,
  'aria-expanded': ariaExpanded,
  'aria-controls': ariaControls,
  'data-testid': dataTestId
}: {
  children: ReactNode
  disabled?: boolean
  open?: boolean
  onClick: () => void
  id?: string
  'aria-label'?: string
  'aria-haspopup'?: 'listbox' | 'menu' | 'dialog' | 'true'
  'aria-expanded'?: boolean
  'aria-controls'?: string
  'data-testid'?: string
}): JSX.Element {
  const { state, eventHandlers } = useInteractiveState(disabled)
  return (
    <button
      type="button"
      id={id}
      aria-label={ariaLabel}
      aria-haspopup={ariaHasPopup}
      aria-expanded={ariaExpanded}
      aria-controls={ariaControls}
      data-testid={dataTestId}
      style={{
        ...pillStyle,
        ...interactiveStyle(state, {
          active: open,
          disabled
        })
      }}
      disabled={disabled}
      onClick={onClick}
      {...eventHandlers}
    >
      {children}
    </button>
  )
}

export function FooterMenuButton({
  children,
  icon,
  checked,
  active,
  disabled,
  onClick
}: {
  children: ReactNode
  icon: ReactNode
  checked?: boolean
  active?: boolean
  disabled?: boolean
  onClick: () => void
}): JSX.Element {
  const { state, eventHandlers } = useInteractiveState(disabled)
  return (
    <button
      type="button"
      style={{
        ...menuButtonStyle,
        ...interactiveStyle(state, { active, disabled })
      }}
      disabled={disabled}
      onClick={onClick}
      {...eventHandlers}
    >
      {icon}
      {children}
      {checked && <Check size={15} strokeWidth={1.8} aria-hidden />}
    </button>
  )
}

export function WorkspaceMenuItem({
  label,
  icon,
  checked,
  disabled,
  onClick
}: {
  label: string
  icon: JSX.Element
  checked: boolean
  disabled?: boolean
  onClick: () => void
}): JSX.Element {
  return (
    <FooterMenuButton
      icon={icon}
      checked={checked}
      disabled={disabled}
      onClick={onClick}
    >
      <span style={{ flex: 1 }}>{label}</span>
    </FooterMenuButton>
  )
}

export function FooterMenuSearchField({
  value,
  placeholder,
  onChange
}: {
  value: string
  placeholder: string
  onChange: (value: string) => void
}): JSX.Element {
  return (
    <div style={{
      display: 'flex',
      alignItems: 'center',
      gap: '8px',
      height: '32px',
      padding: '0 8px',
      color: 'var(--text-dimmed)'
    }}>
      <Search size={14} strokeWidth={1.8} aria-hidden />
      <Input
        bare
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder={placeholder}
        style={{
          flex: 1,
          minWidth: 0,
          background: 'transparent',
          font: 'inherit'
        }}
      />
    </div>
  )
}

export function FooterMenuDivider(): JSX.Element {
  return (
    <div
      style={{
        height: '1px',
        background: 'color-mix(in srgb, var(--text-primary) 9%, transparent)',
        margin: '6px 8px'
      }}
    />
  )
}

import type {
  ChangeEvent,
  CSSProperties,
  JSX,
  MouseEvent as ReactMouseEvent,
  MouseEventHandler,
  ReactNode,
  Ref
} from 'react'
import { ActionTooltip } from './ActionTooltip'

/**
 * Unified text input + trailing action button, rendered as a single
 * pill-shaped control so the action visually lives inside the field.
 *
 * Used for path pickers and similar "input + browse" combos.
 * Focus / hover / invalid states are handled via CSS in tokens.css
 * (`.dc-input-with-action` and its descendant selectors).
 */
export interface InputWithActionProps {
  value: string
  onChange: (event: ChangeEvent<HTMLInputElement>) => void
  placeholder?: string
  id?: string
  mono?: boolean
  disabled?: boolean
  invalid?: boolean
  actionIcon: ReactNode
  actionLabel: string
  onAction: (event: ReactMouseEvent<HTMLButtonElement>) => void
  actionDisabled?: boolean
  onInputClick?: MouseEventHandler<HTMLInputElement>
  inputRef?: Ref<HTMLInputElement>
  'aria-describedby'?: string
}

export function InputWithAction({
  value,
  onChange,
  placeholder,
  id,
  mono = false,
  disabled = false,
  invalid = false,
  actionIcon,
  actionLabel,
  onAction,
  actionDisabled,
  onInputClick,
  inputRef,
  'aria-describedby': ariaDescribedby
}: InputWithActionProps): JSX.Element {
  const isActionDisabled = actionDisabled ?? disabled
  return (
    <div
      className="dc-input-with-action"
      data-invalid={invalid || undefined}
      data-disabled={disabled || undefined}
      style={containerStyle()}
    >
      <input
        id={id}
        ref={inputRef}
        type="text"
        value={value}
        onChange={onChange}
        onClick={onInputClick}
        placeholder={placeholder}
        disabled={disabled}
        aria-describedby={ariaDescribedby}
        style={innerInputStyle(mono)}
      />
      <ActionTooltip
        label={actionLabel}
        disabledReason={isActionDisabled ? actionLabel : undefined}
        placement="top"
      >
        <button
          type="button"
          aria-label={actionLabel}
          disabled={isActionDisabled}
          onClick={onAction}
          style={inlineActionStyle(isActionDisabled)}
        >
          {actionIcon}
        </button>
      </ActionTooltip>
    </div>
  )
}

function containerStyle(): CSSProperties {
  return {
    display: 'flex',
    alignItems: 'stretch',
    width: '100%',
    boxSizing: 'border-box',
    borderRadius: '8px',
    border: '1px solid var(--border-default)',
    background: 'var(--bg-primary)',
    overflow: 'hidden',
    transition: 'border-color 120ms ease, box-shadow 120ms ease'
  }
}

function innerInputStyle(mono: boolean): CSSProperties {
  return {
    flex: 1,
    minWidth: 0,
    padding: '8px 10px',
    fontSize: '13px',
    border: 'none',
    background: 'transparent',
    color: 'var(--text-primary)',
    outline: 'none',
    fontFamily: mono ? 'var(--font-mono)' : undefined
  }
}

function inlineActionStyle(disabled: boolean): CSSProperties {
  return {
    display: 'inline-flex',
    alignItems: 'center',
    justifyContent: 'center',
    width: '34px',
    minWidth: '34px',
    border: 'none',
    borderLeft: '1px solid var(--border-default)',
    background: 'transparent',
    color: disabled ? 'var(--text-dimmed)' : 'var(--text-secondary)',
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.65 : 1,
    padding: 0,
    transition: 'background-color 120ms ease, color 120ms ease',
    outline: 'none'
  }
}

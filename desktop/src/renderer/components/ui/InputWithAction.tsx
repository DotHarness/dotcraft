import type {
  ChangeEvent,
  JSX,
  MouseEvent as ReactMouseEvent,
  MouseEventHandler,
  ReactNode,
  Ref
} from 'react'
import { ActionTooltip } from './ActionTooltip'
import { Input } from './Input'

/**
 * A text input and its trailing action rendered as one pill-shaped control. Focus,
 * hover, and invalid states come from `.dc-input-with-action` in CSS, not from here.
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
    >
      <Input
        id={id}
        ref={inputRef}
        bare
        mono={mono}
        className="dc-input-with-action__input"
        value={value}
        onChange={onChange}
        onClick={onInputClick}
        placeholder={placeholder}
        disabled={disabled}
        aria-describedby={ariaDescribedby}
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
        >
          {actionIcon}
        </button>
      </ActionTooltip>
    </div>
  )
}


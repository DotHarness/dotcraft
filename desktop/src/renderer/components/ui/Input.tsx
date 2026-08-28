import {
  forwardRef,
  type InputHTMLAttributes,
  type JSX,
  type TextareaHTMLAttributes
} from 'react'

/**
 * Size families map to the shared control band (32px). `toolbar` is the catalog
 * top-bar band, shared by every control in that bar including its search field.
 */
export type FieldSize = 'default' | 'toolbar'

interface SharedFieldProps {
  size?: FieldSize
  /**
   * Drop the rest-state border so the focus border is the only frame the field
   * ever paints. For a single prominent input; dense forms stay bordered.
   */
  frameless?: boolean
  /**
   * Strip the frame, fill, and sizing entirely: for the inner input of a
   * composed control whose shell already paints those and owns the focus state.
   */
  bare?: boolean
  /** Marks a validation failure with the shared warning border. */
  invalid?: boolean
  mono?: boolean
}

export interface InputProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, 'size'>, SharedFieldProps {}

export interface TextareaProps
  extends TextareaHTMLAttributes<HTMLTextAreaElement>, SharedFieldProps {}

/**
 * Every Desktop-owned text input should route through this component so its
 * treatments cannot drift per call site; see Inputs in specs/architecture/DESIGN.md.
 * It owns its height and never sets `flex`, so a caller that needs it to stretch
 * inside a row sets `flex: 1` itself.
 */
export const Input = forwardRef<HTMLInputElement, InputProps>(function Input({
  size = 'default',
  frameless = false,
  bare = false,
  invalid = false,
  mono = false,
  type = 'text',
  className,
  ...props
}: InputProps, ref): JSX.Element {
  return (
    <input
      ref={ref}
      type={type}
      data-size={size}
      data-frameless={frameless ? '' : undefined}
      data-bare={bare ? '' : undefined}
      data-invalid={invalid ? '' : undefined}
      data-mono={mono ? '' : undefined}
      aria-invalid={invalid || undefined}
      className={className ? `dc-field ${className}` : 'dc-field'}
      {...props}
    />
  )
})

/** Multi-line counterpart of {@link Input}; grows with `rows` and stays resizable. */
export const Textarea = forwardRef<HTMLTextAreaElement, TextareaProps>(function Textarea({
  size = 'default',
  frameless = false,
  bare = false,
  invalid = false,
  mono = false,
  className,
  ...props
}: TextareaProps, ref): JSX.Element {
  return (
    <textarea
      ref={ref}
      data-size={size}
      data-multiline=""
      data-frameless={frameless ? '' : undefined}
      data-bare={bare ? '' : undefined}
      data-invalid={invalid ? '' : undefined}
      data-mono={mono ? '' : undefined}
      aria-invalid={invalid || undefined}
      className={className ? `dc-field ${className}` : 'dc-field'}
      {...props}
    />
  )
})

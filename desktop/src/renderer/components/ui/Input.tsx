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
   * Drop the rest-state border so the accent focus border is the only frame the
   * field ever paints. For the one prominent message/objective input in an
   * action dialog; dense multi-field forms stay bordered.
   */
  frameless?: boolean
  /**
   * Strip the frame, fill, and sizing entirely: for the inner input of a
   * composed control whose shell already paints those and owns the focus state.
   */
  bare?: boolean
  /** Marks a validation failure with the shared warning border. */
  invalid?: boolean
  /** Monospace value — for paths, commands, templates, and other literal text. */
  mono?: boolean
}

export interface InputProps
  extends Omit<InputHTMLAttributes<HTMLInputElement>, 'size'>, SharedFieldProps {}

export interface TextareaProps
  extends TextareaHTMLAttributes<HTMLTextAreaElement>, SharedFieldProps {}

/**
 * Shared single-line text field. Every Desktop-owned text input should route
 * through this component so height, radius, placeholder, hover, focus, invalid,
 * and disabled treatments cannot drift per call site.
 *
 * The focus affordance is the field's own border, never an outline or ring —
 * see the Inputs section in specs/architecture/DESIGN.md. The component owns its
 * height and never sets `flex`; callers that need it to stretch inside a row put
 * `flex: 1` on the element themselves, which is safe there because the row's
 * main axis is horizontal.
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

import type { CSSProperties, FormEvent, ReactNode, Ref } from 'react'
import { createPortal } from 'react-dom'
import { LayerBoundary } from '../../contexts/LayerContext'

interface ModalDialogProps {
  ref?: Ref<HTMLDivElement>
  titleId?: string
  ariaLabel?: string
  role?: 'dialog' | 'alertdialog'
  width?: number | string
  maxHeight?: number | string
  onDismiss?: () => void
  onSubmit?: () => void
  header?: ReactNode
  error?: ReactNode
  footer?: ReactNode
  children?: ReactNode
  className?: string
  bodyClassName?: string
  scrimStyle?: CSSProperties
  frameStyle?: CSSProperties
}

export function ModalDialog({
  ref,
  titleId,
  ariaLabel,
  role = 'dialog',
  width,
  maxHeight,
  onDismiss,
  onSubmit,
  header,
  error,
  footer,
  children,
  className,
  bodyClassName,
  scrimStyle,
  frameStyle: frameStyleOverride
}: ModalDialogProps): JSX.Element {
  const frameStyle = {
    ...(width === undefined ? {} : { '--dc-modal-width': cssLength(width) }),
    ...(maxHeight === undefined ? {} : { '--dc-modal-max-height': cssLength(maxHeight) }),
    ...frameStyleOverride
  } as CSSProperties
  const content = (
    <>
      {children != null && (
        <div className={bodyClassName ? `dc-modal__body ${bodyClassName}` : 'dc-modal__body'}>{children}</div>
      )}
      {error != null && error !== false && <p className="dc-modal__inset dc-modal__error" role="alert">{error}</p>}
      {footer != null && <div className="dc-modal__inset">{footer}</div>}
    </>
  )
  const dialog = (
    <div
      className="dc-modal-scrim"
      style={scrimStyle}
      onMouseDown={(event) => {
        if (onDismiss && event.target === event.currentTarget) onDismiss()
      }}
    >
      <div
        ref={ref}
        className={className ? `dc-modal ${className}` : 'dc-modal'}
        style={frameStyle}
        role={role}
        aria-modal="true"
        aria-labelledby={titleId}
        aria-label={titleId ? undefined : ariaLabel}
      >
        {header != null && <div className="dc-modal__inset">{header}</div>}
        {onSubmit ? (
          <form
            className="dc-modal__form"
            onSubmit={(event: FormEvent) => {
              event.preventDefault()
              onSubmit()
            }}
          >
            {content}
          </form>
        ) : content}
      </div>
    </div>
  )
  return createPortal(<LayerBoundary>{dialog}</LayerBoundary>, document.body) as JSX.Element
}

function cssLength(value: number | string): string {
  return typeof value === 'number' ? `${value}px` : value
}

export function ModalFooter({ leading, children }: { leading?: ReactNode; children: ReactNode }): JSX.Element {
  return (
    <div className="dc-modal__footer">
      {leading != null && <div className="dc-modal__footer-leading">{leading}</div>}
      {children}
    </div>
  )
}

import { useEffect, useCallback, useRef, useState } from 'react'
import { createPortal } from 'react-dom'
import { LayerBoundary } from '../../contexts/LayerContext'
import { Button } from './Button'

export interface ConfirmDialogOptions {
  title: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  danger?: boolean
  alert?: boolean
  /** A destructive alternative shown before the confirm button; choosing it resolves to `'alternate'`. */
  alternateLabel?: string
}

export type ConfirmDialogChoice = 'confirm' | 'alternate' | 'cancel'

interface ConfirmDialogProps extends ConfirmDialogOptions {
  onConfirm: () => void
  onCancel: () => void
  onAlternate?: () => void
}

export function ConfirmDialog({
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  danger = false,
  alert = false,
  alternateLabel,
  onConfirm,
  onCancel,
  onAlternate
}: ConfirmDialogProps): JSX.Element {
  const initialFocusRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    // Focus cancel by default for safety; an alert has only its confirm button.
    initialFocusRef.current?.focus()

    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onCancel])

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="confirm-title"
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 10000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        backgroundColor: 'var(--overlay-scrim)'
      }}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onCancel()
      }}
    >
      <div
        style={{
          backgroundColor: 'var(--bg-secondary)',
          borderRadius: '10px',
          boxShadow: 'var(--shadow-level-3)',
          padding: '24px',
          width: '360px',
          maxWidth: 'calc(100vw - 48px)'
        }}
        onMouseDown={(e) => e.stopPropagation()}
      >
        <h2
          id="confirm-title"
          style={{
            margin: '0 0 8px',
            fontSize: '15px',
            fontWeight: 600,
            color: 'var(--text-primary)'
          }}
        >
          {title}
        </h2>
        <p
          style={{
            margin: '0 0 20px',
            fontSize: '13px',
            color: 'var(--text-secondary)',
            lineHeight: 1.5,
            whiteSpace: 'pre-line'
          }}
        >
          {message}
        </p>
        <div style={{ display: 'flex', gap: '8px', justifyContent: 'flex-end' }}>
          {!alert && (
            <Button
              ref={initialFocusRef}
              onClick={onCancel}
              variant="secondary"
            >
              {cancelLabel}
            </Button>
          )}
          {alternateLabel && onAlternate && (
            <Button onClick={onAlternate} variant="danger">
              {alternateLabel}
            </Button>
          )}
          <Button
            ref={alert ? initialFocusRef : undefined}
            onClick={onConfirm}
            autoFocus={false}
            variant={danger ? 'danger' : 'primary'}
          >
            {confirmLabel}
          </Button>
        </div>
      </div>
    </div>
  )

  return createPortal(<LayerBoundary>{dialog}</LayerBoundary>, document.body) as JSX.Element
}

// Imperative API: shows a dialog and resolves with true/false, so callers do not
// prop-drill dialog state through the tree.

interface DialogState extends ConfirmDialogOptions {
  resolve: (value: ConfirmDialogChoice) => void
}

export interface ConfirmDialogRequest {
  result: Promise<boolean>
  dismiss(): void
}

type ChoiceDialogPromise = Promise<ConfirmDialogChoice> & {
  dismiss(): void
}

type ConfirmDialogPromise = Promise<boolean> & {
  dismiss(): void
}

type ConfirmDialogWindow = Window & {
  __confirmDialog?: (opts: ConfirmDialogOptions) => ConfirmDialogPromise
  __choiceDialog?: (opts: ConfirmDialogOptions) => ChoiceDialogPromise
}

/**
 * Global confirm dialog host — mount this once at the app root level.
 * Renders the dialog when triggered by `useConfirmDialog()`.
 */
export function ConfirmDialogHost(): JSX.Element | null {
  const [state, setState] = useState<DialogState | null>(null)

  // Expose the trigger function globally so useConfirmDialog() can reach it
  useEffect(() => {
    const choose = (opts: ConfirmDialogOptions): ChoiceDialogPromise => {
      let settle!: (value: ConfirmDialogChoice) => void
      const result = new Promise<ConfirmDialogChoice>((resolve) => {
        settle = resolve
        setState({ ...opts, resolve })
      })
      return Object.assign(result, {
        dismiss() {
          setState((current) => {
            if (current?.resolve !== settle) return current
            settle('cancel')
            return null
          })
        }
      })
    }
    const host = window as ConfirmDialogWindow
    host.__choiceDialog = choose
    host.__confirmDialog = (opts) => {
      const choice = choose(opts)
      return Object.assign(choice.then((value) => value === 'confirm'), { dismiss: choice.dismiss })
    }
    return () => {
      delete host.__confirmDialog
      delete host.__choiceDialog
    }
  }, [])

  if (!state) return null

  function finish(result: ConfirmDialogChoice): void {
    state!.resolve(result)
    setState(null)
  }

  return (
    <ConfirmDialog
      {...state}
      onConfirm={() => finish('confirm')}
      onCancel={() => finish('cancel')}
      onAlternate={state.alternateLabel ? () => finish('alternate') : undefined}
    />
  )
}

/**
 * Returns a function to imperatively show a confirmation dialog.
 * Requires `<ConfirmDialogHost />` to be mounted at the app root.
 */
export function useConfirmDialog(): (opts: ConfirmDialogOptions) => Promise<boolean> {
  return useCallback(
    (opts: ConfirmDialogOptions) => (window as ConfirmDialogWindow).__confirmDialog!(opts),
    []
  )
}

/** Shows a dialog whose `alternateLabel` adds a third choice. */
export function useChoiceDialog(): (opts: ConfirmDialogOptions) => Promise<ConfirmDialogChoice> {
  return useCallback(
    (opts: ConfirmDialogOptions) => (window as ConfirmDialogWindow).__choiceDialog!(opts),
    []
  )
}

export function requestConfirmDialog(options: ConfirmDialogOptions): ConfirmDialogRequest {
  const result = (window as ConfirmDialogWindow).__confirmDialog!(options)
  return { result, dismiss: result.dismiss }
}

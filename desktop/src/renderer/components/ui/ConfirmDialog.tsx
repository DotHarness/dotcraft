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
}

interface ConfirmDialogProps extends ConfirmDialogOptions {
  onConfirm: () => void
  onCancel: () => void
}

/**
 * Centered modal confirmation dialog.
 * Spec §11
 */
export function ConfirmDialog({
  title,
  message,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  danger = false,
  onConfirm,
  onCancel
}: ConfirmDialogProps): JSX.Element {
  const cancelButtonRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    // Focus cancel button by default for safety
    cancelButtonRef.current?.focus()

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
        // Click backdrop to cancel
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
          <Button
            ref={cancelButtonRef}
            onClick={onCancel}
            variant="secondary"
          >
            {cancelLabel}
          </Button>
          <Button
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

// ---------------------------------------------------------------------------
// Imperative API: useConfirmDialog()
// Returns a function that shows a dialog and resolves with true/false.
// This is used in ThreadEntry to avoid prop-drilling a dialog state through
// the entire component tree.
// ---------------------------------------------------------------------------

interface DialogState extends ConfirmDialogOptions {
  resolve: (value: boolean) => void
}

/**
 * Global confirm dialog host — mount this once at the app root level.
 * Renders the dialog when triggered by `useConfirmDialog()`.
 */
export function ConfirmDialogHost(): JSX.Element | null {
  const [state, setState] = useState<DialogState | null>(null)

  // Expose the trigger function globally so useConfirmDialog() can reach it
  useEffect(() => {
    ;(window as Window & { __confirmDialog?: (opts: ConfirmDialogOptions) => Promise<boolean> }).__confirmDialog = (opts) =>
      new Promise<boolean>((resolve) => {
        setState({ ...opts, resolve })
      })
    return () => {
      delete (window as Window & { __confirmDialog?: unknown }).__confirmDialog
    }
  }, [])

  if (!state) return null

  function finish(result: boolean): void {
    state!.resolve(result)
    setState(null)
  }

  return (
    <ConfirmDialog
      {...state}
      onConfirm={() => finish(true)}
      onCancel={() => finish(false)}
    />
  )
}

/**
 * Returns a function to imperatively show a confirmation dialog.
 * Requires `<ConfirmDialogHost />` to be mounted at the app root.
 */
export function useConfirmDialog(): (opts: ConfirmDialogOptions) => Promise<boolean> {
  return useCallback((opts: ConfirmDialogOptions) => {
    const trigger = (window as Window & { __confirmDialog?: (opts: ConfirmDialogOptions) => Promise<boolean> }).__confirmDialog
    if (!trigger) {
      console.warn('ConfirmDialogHost is not mounted')
      return Promise.resolve(false)
    }
    return trigger(opts)
  }, [])
}

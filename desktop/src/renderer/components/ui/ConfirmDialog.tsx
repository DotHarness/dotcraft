import { useEffect, useCallback, useId, useRef, useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { Button } from './Button'
import { ModalDialog, ModalFooter } from './ModalDialog'
import styles from './ConfirmDialog.module.css'

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
  cancelLabel,
  danger = false,
  alert = false,
  alternateLabel,
  onConfirm,
  onCancel,
  onAlternate
}: ConfirmDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  const initialFocusRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    initialFocusRef.current?.focus()

    function handleKeyDown(e: KeyboardEvent): void {
      if (e.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onCancel])

  return (
    <ModalDialog
      titleId={titleId}
      width={360}
      onDismiss={onCancel}
      header={<h2 id={titleId} className={styles.title}>{title}</h2>}
      footer={(
        <ModalFooter>
          {!alert && (
            <Button ref={initialFocusRef} onClick={onCancel} variant="secondary">
              {cancelLabel ?? t('common.cancel')}
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
        </ModalFooter>
      )}
    >
      <p className={styles.message}>{message}</p>
    </ModalDialog>
  )
}

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

export function ConfirmDialogHost(): JSX.Element | null {
  const [state, setState] = useState<DialogState | null>(null)

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

export function useConfirmDialog(): (opts: ConfirmDialogOptions) => Promise<boolean> {
  return useCallback(
    (opts: ConfirmDialogOptions) => (window as ConfirmDialogWindow).__confirmDialog!(opts),
    []
  )
}

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

import { useEffect, useRef, type JSX, type ReactNode } from 'react'
import { AlertTriangle } from 'lucide-react'

import { ModalDialog, ModalFooter } from '../../../ui/ModalDialog'
import * as s from '../connections/connectionsStyles'

const FOCUSABLE = 'button:not([disabled]), input:not([disabled])'

export function PhonesDialog({
  titleId,
  header,
  footer,
  onClose,
  onSubmit,
  children
}: {
  titleId: string
  header: ReactNode
  footer: ReactNode
  onClose: () => void
  onSubmit?: () => void
  children: ReactNode
}): JSX.Element {
  const dialogRef = useRef<HTMLDivElement>(null)
  const closeRef = useRef(onClose)
  closeRef.current = onClose

  useEffect(() => {
    const opener = document.activeElement
    const dialog = dialogRef.current
    ;(dialog?.querySelector<HTMLElement>('[data-autofocus]') ?? dialog?.querySelector<HTMLElement>(FOCUSABLE))?.focus()
    return () => {
      if (opener instanceof HTMLElement && opener.isConnected) opener.focus()
    }
  }, [])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') {
        closeRef.current()
        return
      }
      if (event.key !== 'Tab' || !dialogRef.current) return
      const focusable = Array.from(dialogRef.current.querySelectorAll<HTMLElement>(FOCUSABLE))
      if (focusable.length === 0) return
      const first = focusable[0]
      const last = focusable[focusable.length - 1]
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault()
        last.focus()
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault()
        first.focus()
      }
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [])

  return (
    <ModalDialog
      ref={dialogRef}
      titleId={titleId}
      width={460}
      onDismiss={onClose}
      onSubmit={onSubmit}
      header={header}
      footer={<ModalFooter>{footer}</ModalFooter>}
    >
      {children}
    </ModalDialog>
  )
}

export function PhonesDialogFailure({ title, reason }: { title: string; reason: string }): JSX.Element {
  return (
    <div style={s.banner} role="alert">
      <span className="dc-satellite-invite-banner__glyph" aria-hidden>
        <AlertTriangle size={20} />
      </span>
      <div className="dc-add-phone__banner-text">
        <div className="dc-satellite-invite-banner__text">{title}</div>
        <div className="dc-satellite-invite-banner__reason">{reason}</div>
      </div>
    </div>
  )
}

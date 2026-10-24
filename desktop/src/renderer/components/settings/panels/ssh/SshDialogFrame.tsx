import { useEffect, useRef, type JSX, type ReactNode } from 'react'

import { ModalDialog } from '../../../ui/ModalDialog'

const FOCUSABLE = 'button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])'

function isTopmost(dialog: HTMLElement | null): boolean {
  if (!dialog || document.querySelector('[role="menu"]')) return false
  const modals = document.querySelectorAll('[aria-modal="true"]')
  return modals[modals.length - 1] === dialog
}

export function SshDialogFrame({
  titleId,
  wide = false,
  onClose,
  header,
  footer,
  children
}: {
  titleId: string
  wide?: boolean
  onClose: () => void
  header: ReactNode
  footer: ReactNode
  children: ReactNode
}): JSX.Element {
  const dialogRef = useRef<HTMLDivElement>(null)
  const closeRef = useRef(onClose)
  closeRef.current = onClose

  useEffect(() => {
    const opener = document.activeElement
    dialogRef.current?.querySelector<HTMLElement>(FOCUSABLE)?.focus()
    return () => {
      if (opener instanceof HTMLElement && opener.isConnected) opener.focus()
    }
  }, [])

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (!isTopmost(dialogRef.current)) return
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
      width={wide ? 580 : 480}
      onDismiss={onClose}
      header={header}
      footer={footer}
    >
      {children}
    </ModalDialog>
  )
}

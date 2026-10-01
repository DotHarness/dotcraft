import { useEffect, useRef, type JSX, type ReactNode } from 'react'
import { createPortal } from 'react-dom'

import { LayerBoundary } from '../../../../contexts/LayerContext'

const FOCUSABLE = 'button:not([disabled]), input:not([disabled]), [tabindex]:not([tabindex="-1"])'

function isTopmost(scrim: HTMLElement | null): boolean {
  if (!scrim || document.querySelector('[role="menu"]')) return false
  const modals = document.querySelectorAll('[aria-modal="true"]')
  return modals[modals.length - 1] === scrim
}

export function SshDialogFrame({
  titleId,
  wide = false,
  onClose,
  children
}: {
  titleId: string
  wide?: boolean
  onClose: () => void
  children: ReactNode
}): JSX.Element {
  const scrimRef = useRef<HTMLDivElement>(null)
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
      if (!isTopmost(scrimRef.current)) return
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

  return createPortal(
    <LayerBoundary>
      <div
        ref={scrimRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="dc-ssh-scrim"
        onMouseDown={(event) => {
          if (event.target === event.currentTarget) onClose()
        }}
      >
        <div ref={dialogRef} className="dc-ssh-dialog" data-wide={wide || undefined}>
          {children}
        </div>
      </div>
    </LayerBoundary>,
    document.body
  ) as JSX.Element
}

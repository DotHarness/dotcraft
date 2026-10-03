import { useEffect, useRef, type JSX, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { AlertTriangle } from 'lucide-react'

import { LayerBoundary } from '../../../../contexts/LayerContext'
import * as s from '../connections/connectionsStyles'

const FOCUSABLE = 'button:not([disabled]), input:not([disabled])'

export function PhonesDialog({
  titleId,
  onClose,
  children
}: {
  titleId: string
  onClose: () => void
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

  return createPortal(
    <LayerBoundary>
      <div
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        className="dc-satellite-invite-scrim"
        onMouseDown={(event) => {
          if (event.target === event.currentTarget) onClose()
        }}
      >
        <div ref={dialogRef} className="dc-satellite-invite-dialog" onMouseDown={(event) => event.stopPropagation()}>
          {children}
        </div>
      </div>
    </LayerBoundary>,
    document.body
  ) as JSX.Element
}

export function PhonesDialogFailure({ title, reason }: { title: string; reason: string }): JSX.Element {
  return (
    <div className="dc-satellite-invite-banner" style={s.banner} role="alert">
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

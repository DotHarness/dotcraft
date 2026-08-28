/**
 * The modal shell only; the input and results live in `QuickOpenContent`, which the
 * Changes-header "Jump to file" popover reuses.
 */
import { useCallback, useRef, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { QuickOpenContent } from './QuickOpenContent'

interface QuickOpenDialogProps {
  onClose: () => void
  anchorRef?: React.RefObject<HTMLElement | null>
}

export function QuickOpenDialog({ onClose, anchorRef }: QuickOpenDialogProps): JSX.Element {
  const t = useT()
  const setQuickOpenVisible = useUIStore((s) => s.setQuickOpenVisible)
  const dialogRef = useRef<HTMLDivElement>(null)

  const close = useCallback((): void => {
    setQuickOpenVisible(false)
    onClose()
    anchorRef?.current?.focus()
  }, [anchorRef, onClose, setQuickOpenVisible])

  const handleBackdropClick = (e: React.MouseEvent<HTMLDivElement>): void => {
    if (e.target === e.currentTarget) close()
  }

  return (
    <div
      role="dialog"
      aria-modal="true"
      aria-label={t('quickOpen.title')}
      onClick={handleBackdropClick}
      style={{
        position: 'fixed',
        inset: 0,
        zIndex: 2000,
        display: 'flex',
        alignItems: 'flex-start',
        justifyContent: 'center',
        paddingTop: '12vh',
        backgroundColor: 'rgba(0,0,0,0.5)'
      }}
    >
      <div
        ref={dialogRef}
        style={{
          width: '560px',
          maxWidth: 'calc(100vw - 48px)',
          backgroundColor: 'var(--bg-elevated, #1e1e1e)',
          border: '1px solid var(--border-default)',
          borderRadius: '8px',
          boxShadow: '0 8px 32px rgba(0,0,0,0.4)',
          overflow: 'hidden',
          display: 'flex',
          flexDirection: 'column'
        }}
      >
        <QuickOpenContent onClose={close} />
      </div>
    </div>
  )
}

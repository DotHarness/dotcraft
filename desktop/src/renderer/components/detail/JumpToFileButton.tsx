/**
 * Hosts the shared `QuickOpenContent` finder in an anchored popover; the same
 * finder is reachable globally via Cmd/Ctrl+P.
 */
import { useEffect, useRef, useState, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { FileSearch } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { IconButton } from '../ui/IconButton'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { QuickOpenContent } from './QuickOpenContent'

const POPOVER_WIDTH = 420

export function JumpToFileButton(): JSX.Element {
  const t = useT()
  const buttonRef = useRef<HTMLButtonElement>(null)
  const popoverRef = useRef<HTMLDivElement>(null)
  const [open, setOpen] = useState(false)
  const [anchor, setAnchor] = useState<{ top: number; right: number } | null>(null)

  useEffect(() => {
    if (!open) return
    function handlePointerDown(event: MouseEvent): void {
      const target = event.target as Node
      if (popoverRef.current?.contains(target) || buttonRef.current?.contains(target)) return
      setOpen(false)
    }
    document.addEventListener('mousedown', handlePointerDown)
    return () => {
      document.removeEventListener('mousedown', handlePointerDown)
    }
  }, [open])

  function toggleOpen(): void {
    const rect = buttonRef.current?.getBoundingClientRect()
    if (rect) {
      setAnchor({ top: rect.bottom + 4, right: window.innerWidth - rect.right })
    }
    setOpen((current) => !current)
  }

  const label = t('changes.jumpToFile')

  const popover = open && anchor ? createPortal(
    <div
      ref={popoverRef}
      role="dialog"
      aria-label={label}
      style={{ ...popoverStyle, top: anchor.top, right: anchor.right, width: POPOVER_WIDTH }}
      onContextMenu={(event) => event.preventDefault()}
    >
      <QuickOpenContent onClose={() => setOpen(false)} placeholder={label} resultsMaxHeight={280} />
    </div>,
    document.body
  ) : null

  return (
    <>
      <IconButton
        ref={buttonRef}
        size={28}
        label={label}
        tooltipLabel={label}
        tooltipPlacement="bottom"
        shortcut={ACTION_SHORTCUTS.quickOpen}
        aria-haspopup="dialog"
        aria-expanded={open}
        onClick={toggleOpen}
        icon={<FileSearch size={16} aria-hidden style={{ display: 'block' }} />}
      />
      {popover}
    </>
  )
}

const popoverStyle: CSSProperties = {
  position: 'fixed',
  maxWidth: 'calc(100vw - 24px)',
  border: 'none',
  borderRadius: '10px',
  background: 'var(--glass-surface-strong)',
  boxShadow: 'var(--glass-shadow-soft)',
  backdropFilter: 'var(--glass-blur)',
  WebkitBackdropFilter: 'var(--glass-blur)',
  overflow: 'hidden',
  display: 'flex',
  flexDirection: 'column',
  zIndex: 9999
}

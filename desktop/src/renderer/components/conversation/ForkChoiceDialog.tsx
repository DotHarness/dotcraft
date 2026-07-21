/**
 * Fork-target chooser shown when forking from an *earlier* (non-last) turn.
 *
 * Forking from the last turn forks straight into a local chat (no prompt). For
 * earlier turns the destination is ambiguous — the working tree may have moved
 * on since that message — so we let the user pick: a new local chat (same
 * workspace) or a fresh git worktree. Mirrors the app's centered-modal pattern
 * (ConfirmDialog) and is rendered through a portal.
 */
import { useEffect, useState, type CSSProperties, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { ArrowRightLeft, GitBranch, Laptop } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { ModalHeader } from '../ui/ModalHeader'
import type { ThreadForkMode } from '../../utils/threadFork'

interface ForkChoiceDialogProps {
  onChoose: (mode: ThreadForkMode) => void
  onCancel: () => void
}

export function ForkChoiceDialog({ onChoose, onCancel }: ForkChoiceDialogProps): JSX.Element {
  const t = useT()

  useEffect(() => {
    function handleKeyDown(event: KeyboardEvent): void {
      if (event.key === 'Escape') onCancel()
    }
    document.addEventListener('keydown', handleKeyDown)
    return () => document.removeEventListener('keydown', handleKeyDown)
  }, [onCancel])

  const dialog = (
    <div
      role="dialog"
      aria-modal="true"
      aria-labelledby="fork-choice-title"
      style={overlayStyle}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onCancel()
      }}
    >
      <div style={cardStyle} onMouseDown={(event) => event.stopPropagation()}>
        <ModalHeader
          icon={<GitBranch size={18} strokeWidth={2} aria-hidden />}
          title={t('fork.choice.title')}
          titleId="fork-choice-title"
          description={t('fork.choice.description')}
        />

        <div style={optionsStyle}>
          <ForkOption
            icon={<Laptop size={18} strokeWidth={2} aria-hidden />}
            title={t('fork.intoLocal')}
            subtitle={t('fork.choice.localDesc')}
            onClick={() => onChoose('local')}
          />
          <ForkOption
            icon={<ArrowRightLeft size={18} strokeWidth={2} aria-hidden />}
            title={t('fork.intoWorktree')}
            subtitle={t('fork.choice.worktreeDesc')}
            onClick={() => onChoose('worktree')}
          />
        </div>
      </div>
    </div>
  )

  return createPortal(dialog, document.body) as JSX.Element
}

function ForkOption({
  icon,
  title,
  subtitle,
  onClick
}: {
  icon: ReactNode
  title: string
  subtitle: string
  onClick: () => void
}): JSX.Element {
  const [hovered, setHovered] = useState(false)
  return (
    <button
      type="button"
      onClick={onClick}
      onMouseEnter={() => setHovered(true)}
      onMouseLeave={() => setHovered(false)}
      style={{
        ...optionStyle,
        background: hovered ? 'var(--bg-tertiary)' : 'transparent',
        borderColor: hovered ? 'var(--border-active)' : 'var(--border-default)'
      }}
    >
      <span style={optionIconStyle}>{icon}</span>
      <span style={optionTextStyle}>
        <span style={optionTitleStyle}>{title}</span>
        <span style={optionSubtitleStyle}>{subtitle}</span>
      </span>
    </button>
  )
}

const overlayStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 10000,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  backgroundColor: 'var(--overlay-scrim)'
}

const cardStyle: CSSProperties = {
  width: '420px',
  maxWidth: 'calc(100vw - 48px)',
  boxSizing: 'border-box',
  padding: '20px',
  borderRadius: '12px',
  background: 'var(--bg-secondary)',
  boxShadow: 'var(--shadow-level-3)',
  display: 'flex',
  flexDirection: 'column'
}

const optionsStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '8px'
}

const optionStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '12px',
  width: '100%',
  padding: '12px',
  borderRadius: '10px',
  border: '1px solid var(--border-default)',
  cursor: 'pointer',
  textAlign: 'left',
  font: 'inherit',
  transition: 'background-color 100ms ease, border-color 100ms ease'
}

const optionIconStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: '34px',
  height: '34px',
  flexShrink: 0,
  borderRadius: '8px',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-primary)'
}

const optionTextStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  gap: '2px',
  minWidth: 0
}

const optionTitleStyle: CSSProperties = {
  fontSize: '13px',
  fontWeight: 600,
  color: 'var(--text-primary)'
}

const optionSubtitleStyle: CSSProperties = {
  fontSize: '12px',
  color: 'var(--text-secondary)'
}

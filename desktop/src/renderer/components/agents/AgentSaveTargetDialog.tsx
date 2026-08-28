/**
 * The destination is asked only at Create time, not carried as a persistent toggle.
 * "User" writes to the user-global `.craft`, "Workspace" to the project's own.
 */
import { useEffect, useState, type CSSProperties, type ReactNode } from 'react'
import { createPortal } from 'react-dom'
import { FolderGit2, Save, User } from 'lucide-react'
import { ModalHeader } from '../ui/ModalHeader'
import type { SaveTarget } from './agentProfileDraft'

interface AgentSaveTargetDialogProps {
  name: string
  onChoose: (target: SaveTarget) => void
  onCancel: () => void
}

export function AgentSaveTargetDialog({ name, onChoose, onCancel }: AgentSaveTargetDialogProps): JSX.Element {
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
      aria-labelledby="agent-save-title"
      style={overlayStyle}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget) onCancel()
      }}
    >
      <div style={cardStyle} onMouseDown={(event) => event.stopPropagation()}>
        <ModalHeader
          icon={<Save size={18} strokeWidth={2} aria-hidden />}
          title="Save this agent"
          titleId="agent-save-title"
          description={name ? `Choose where to keep “${name}”.` : 'Choose where to keep this agent.'}
        />

        <div style={optionsStyle}>
          <SaveOption
            icon={<User size={18} strokeWidth={2} aria-hidden />}
            title="User"
            subtitle="Available across all your workspaces"
            onClick={() => onChoose('user')}
          />
          <SaveOption
            icon={<FolderGit2 size={18} strokeWidth={2} aria-hidden />}
            title="Workspace"
            subtitle="Only in this workspace"
            onClick={() => onChoose('workspace')}
          />
        </div>

        <button type="button" onClick={onCancel} style={cancelStyle}>
          Cancel
        </button>
      </div>
    </div>
  )

  return createPortal(dialog, document.body) as JSX.Element
}

function SaveOption({
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

const cancelStyle: CSSProperties = {
  marginTop: '14px',
  width: '100%',
  padding: '8px',
  borderRadius: '8px',
  border: '1px solid var(--border-default)',
  background: 'transparent',
  color: 'var(--text-primary)',
  fontSize: '13px',
  cursor: 'pointer'
}

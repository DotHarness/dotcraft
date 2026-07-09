import type { CSSProperties, ReactNode } from 'react'
import { X } from 'lucide-react'

interface ModalHeaderProps {
  /**
   * Identity glyph for the dialog (e.g. a lucide icon). Rendered inside the
   * neutral badge, so it should be an unsized/18px icon — the badge owns the box.
   */
  icon: ReactNode
  title: string
  /** id applied to the title, so the dialog can point aria-labelledby at it. */
  titleId?: string
  /** Optional one or two line supporting copy under the title. */
  description?: ReactNode
  /** When provided, renders a borderless close button in the top-right. */
  onClose?: () => void
  closeLabel?: string
  /** Merged onto the header container (e.g. to tune the gap before the body). */
  style?: CSSProperties
}

/**
 * Shared dialog header: a neutral badged identity icon with the title below it
 * (and an optional description), plus an optional borderless close button.
 *
 * This is the single source of truth for the "icon badge + title" lockup so
 * every dialog that carries an identity icon reads as one family. See the
 * Dialog Headers section in specs/architecture/DESIGN.md.
 */
export function ModalHeader({
  icon,
  title,
  titleId,
  description,
  onClose,
  closeLabel,
  style
}: ModalHeaderProps): JSX.Element {
  return (
    <div style={{ ...containerStyle, ...style }}>
      <div style={topRowStyle}>
        <span style={badgeStyle}>{icon}</span>
        {onClose && (
          <button
            type="button"
            aria-label={closeLabel}
            onClick={onClose}
            style={closeStyle}
            onMouseEnter={(e) => {
              e.currentTarget.style.backgroundColor = 'var(--bg-tertiary)'
              e.currentTarget.style.color = 'var(--text-primary)'
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.backgroundColor = 'transparent'
              e.currentTarget.style.color = 'var(--text-secondary)'
            }}
          >
            <X size={16} aria-hidden />
          </button>
        )}
      </div>
      <h2 id={titleId} style={titleStyle}>
        {title}
      </h2>
      {description != null && description !== '' && <p style={descriptionStyle}>{description}</p>}
    </div>
  )
}

const containerStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  marginBottom: '16px'
}

const topRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'flex-start',
  justifyContent: 'space-between',
  marginBottom: '12px'
}

const badgeStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  width: '36px',
  height: '36px',
  flexShrink: 0,
  borderRadius: '9px',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-secondary)'
}

const closeStyle: CSSProperties = {
  width: '30px',
  height: '30px',
  flexShrink: 0,
  borderRadius: '8px',
  border: 'none',
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  transition: 'background-color 100ms ease, color 100ms ease'
}

const titleStyle: CSSProperties = {
  margin: 0,
  fontSize: '15px',
  fontWeight: 600,
  lineHeight: 1.3,
  color: 'var(--text-primary)'
}

const descriptionStyle: CSSProperties = {
  margin: '6px 0 0',
  fontSize: '12.5px',
  lineHeight: 1.5,
  color: 'var(--text-secondary)'
}

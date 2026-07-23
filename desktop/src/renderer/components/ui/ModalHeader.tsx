import type { CSSProperties, ReactNode } from 'react'
import { X } from 'lucide-react'
import { IconButton } from './IconButton'

interface ModalHeaderProps {
  /**
   * Identity glyph for the dialog (e.g. a lucide icon). Rendered inside the
   * neutral badge, so it should be an unsized/18px icon — the badge owns the box.
   * With `badgedIcon={false}` it is rendered as-is and must supply its own box.
   */
  icon: ReactNode
  /**
   * Set false when the subject has its own product artwork — a skill or plugin
   * avatar is already a badge, and nesting it inside the neutral one reads as two
   * boxes. It must match the badge's footprint. See DESIGN.md Dialog Headers.
   */
  badgedIcon?: boolean
  title: string
  /** Badge or marker shown beside the title, e.g. a variant marker. */
  titleAdornment?: ReactNode
  /** id applied to the title, so the dialog can point aria-labelledby at it. */
  titleId?: string
  /** Optional one or two line supporting copy under the title. */
  description?: ReactNode
  /** When provided, renders a borderless close button in the top-right. */
  onClose?: () => void
  closeLabel?: string
  /** Extra controls for the badge row, placed before the close button. */
  actions?: ReactNode
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
  badgedIcon = true,
  title,
  titleAdornment,
  titleId,
  description,
  onClose,
  closeLabel,
  actions,
  style
}: ModalHeaderProps): JSX.Element {
  return (
    <div style={{ ...containerStyle, ...style }}>
      <div style={topRowStyle}>
        {badgedIcon ? <span style={badgeStyle}>{icon}</span> : icon}
        <span style={actionRowStyle}>
          {actions}
          {onClose && (
            <IconButton
              icon={<X size={16} aria-hidden />}
              label={closeLabel ?? 'Close'}
              size={30}
              onClick={onClose}
            />
          )}
        </span>
      </div>
      <div style={titleRowStyle}>
        <h2 id={titleId} style={titleStyle}>
          {title}
        </h2>
        {titleAdornment}
      </div>
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

const actionRowStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: '4px',
  flexShrink: 0
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

const titleRowStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: '8px',
  minWidth: 0
}

const titleStyle: CSSProperties = {
  margin: 0,
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
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

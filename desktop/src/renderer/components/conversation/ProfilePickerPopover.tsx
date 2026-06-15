import { useEffect, useState, type CSSProperties, type JSX } from 'react'
import { createPortal } from 'react-dom'
import { X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { RobotAvatar } from '../agents/RobotAvatar'
import { resolveProfileAvatar, type AvatarSpec } from '../agents/agentAvatar'

interface ProfileEntry {
  id: string
  description?: string
  source: string
  valid?: boolean
  shadowed?: boolean
  /** Avatar the user configured in the builder (packed number or spec); honored over the derived one. */
  avatar?: number | AvatarSpec
}

interface ProfilePickerPopoverProps {
  visible: boolean
  activeProfileId?: string
  onPick: (profileId: string) => void
  onDismiss: () => void
}

/**
 * Modal list of the workspace's Agent Profiles, opened from the `/Profile` composer command.
 * Selecting one applies it to the active thread (the caller wires `agent/profiles/refreshThread`).
 */
export function ProfilePickerPopover({ visible, activeProfileId, onPick, onDismiss }: ProfilePickerPopoverProps): JSX.Element | null {
  const t = useT()
  const [profiles, setProfiles] = useState<ProfileEntry[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [hoveredId, setHoveredId] = useState<string | null>(null)

  useEffect(() => {
    if (!visible) return undefined
    let cancelled = false
    setLoading(true)
    setError(null)
    void window.api.appServer
      .sendRequest('agent/profiles/list', { includeInvalid: false })
      .then((res) => {
        if (cancelled) return
        const list = ((res as { profiles?: ProfileEntry[] }).profiles ?? []).filter((p) => !p.shadowed && p.valid !== false)
        setProfiles(list)
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(err instanceof Error ? err.message : String(err))
      })
      .finally(() => {
        if (!cancelled) setLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [visible])

  useEffect(() => {
    if (!visible) return undefined
    function onKey(event: KeyboardEvent): void {
      if (event.key === 'Escape') onDismiss()
    }
    document.addEventListener('keydown', onKey, true)
    return () => document.removeEventListener('keydown', onKey, true)
  }, [visible, onDismiss])

  if (!visible) return null

  return createPortal(
    <div style={SCRIM_STYLE} onMouseDown={onDismiss}>
      <div style={CARD_STYLE} role="dialog" aria-modal="true" onMouseDown={(event) => event.stopPropagation()}>
        <div style={HEAD_STYLE}>
          <span>{t('composer.profile.pickTitle')}</span>
          <button type="button" style={CLOSE_STYLE} aria-label={t('composer.customPill.aria')} onClick={onDismiss}>
            <X size={16} aria-hidden />
          </button>
        </div>
        <div style={BODY_STYLE}>
          {loading ? (
            <div style={STATE_STYLE}>{t('composer.profile.loading')}</div>
          ) : error ? (
            <div style={STATE_STYLE}>{error}</div>
          ) : profiles.length === 0 ? (
            <div style={STATE_STYLE}>{t('composer.profile.empty')}</div>
          ) : (
            profiles.map((profile) => (
              <button
                key={`${profile.source}:${profile.id}`}
                type="button"
                style={itemStyle(profile.id === activeProfileId, profile.id === hoveredId)}
                onClick={() => onPick(profile.id)}
                onMouseEnter={() => setHoveredId(profile.id)}
                onMouseLeave={() => setHoveredId((prev) => (prev === profile.id ? null : prev))}
              >
                <span style={AVATAR_STYLE} aria-hidden>
                  <RobotAvatar spec={resolveProfileAvatar(profile.id, profile.avatar)} size={30} />
                </span>
                <span style={COPY_STYLE}>
                  <span style={NAME_STYLE}>{profile.id}</span>
                  {profile.description && <span style={DESC_STYLE}>{profile.description}</span>}
                </span>
              </button>
            ))
          )}
        </div>
      </div>
    </div>,
    document.body
  )
}

const SCRIM_STYLE: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 1100,
  display: 'grid',
  placeItems: 'center',
  background: 'color-mix(in srgb, var(--bg-primary) 55%, transparent)',
  padding: 24
}

const CARD_STYLE: CSSProperties = {
  width: 'min(460px, 100%)',
  maxHeight: '70vh',
  display: 'flex',
  flexDirection: 'column',
  border: '1px solid var(--border-default)',
  borderRadius: 16,
  background: 'var(--bg-elevated)',
  boxShadow: 'var(--shadow-lg)',
  overflow: 'hidden'
}

const HEAD_STYLE: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  padding: '14px 16px',
  fontSize: 14,
  fontWeight: 650,
  color: 'var(--text-primary)',
  borderBottom: '1px solid var(--border-default)'
}

const CLOSE_STYLE: CSSProperties = {
  width: 26,
  height: 26,
  display: 'grid',
  placeItems: 'center',
  border: 0,
  borderRadius: 8,
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer'
}

const BODY_STYLE: CSSProperties = {
  flex: '1 1 auto',
  minHeight: 0,
  overflowY: 'auto',
  padding: 8,
  display: 'flex',
  flexDirection: 'column',
  gap: 2
}

const STATE_STYLE: CSSProperties = {
  padding: '24px 12px',
  textAlign: 'center',
  fontSize: 13,
  color: 'var(--text-secondary)'
}

function itemStyle(active: boolean, hovered: boolean): CSSProperties {
  return {
    display: 'flex',
    alignItems: 'center',
    gap: 11,
    width: '100%',
    padding: '9px 11px',
    border: '1px solid transparent',
    borderRadius: 10,
    // Active profile keeps its accent highlight; other rows get the shared menu
    // row hover (--sidebar-control-hover, as ContextMenu) — previously rows had
    // no hover feedback at all.
    background: active
      ? 'color-mix(in srgb, var(--accent) 14%, transparent)'
      : hovered
        ? 'var(--sidebar-control-hover)'
        : 'transparent',
    borderColor: active ? 'color-mix(in srgb, var(--accent) 45%, var(--border-active))' : 'transparent',
    color: 'inherit',
    cursor: 'pointer',
    textAlign: 'left',
    transition: 'background-color 120ms ease'
  }
}

const AVATAR_STYLE: CSSProperties = {
  flex: '0 0 auto',
  display: 'inline-flex'
}

const COPY_STYLE: CSSProperties = {
  flex: '1 1 auto',
  minWidth: 0,
  display: 'flex',
  flexDirection: 'column',
  gap: 2
}

const NAME_STYLE: CSSProperties = {
  fontSize: 13.5,
  fontWeight: 650,
  color: 'var(--text-primary)'
}

const DESC_STYLE: CSSProperties = {
  fontSize: 12,
  color: 'var(--text-secondary)',
  lineHeight: 1.45,
  overflow: 'hidden',
  display: '-webkit-box',
  WebkitLineClamp: 2,
  WebkitBoxOrient: 'vertical'
}

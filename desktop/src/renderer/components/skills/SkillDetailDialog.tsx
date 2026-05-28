import { useEffect, useState } from 'react'
import { Ellipsis, MessageCircle, X } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import type { SkillEntry } from '../../stores/skillsStore'
import { dirname } from '../../utils/path'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { ActionTooltip } from '../ui/ActionTooltip'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { PillSwitch } from '../ui/PillSwitch'
import { SkillAvatar } from './SkillAvatar'
import { VariantBadge } from './VariantBadge'

interface SkillDetailDialogProps {
  skill: SkillEntry
  markdownBody: string
  loading: boolean
  onClose: () => void
  onToggleEnabled: (enabled: boolean) => void
  onTryInChat: () => void
  onRestoreOriginal?: () => void
  onUninstall?: () => void
  showToggle?: boolean
}

export function SkillDetailDialog({
  skill,
  markdownBody,
  loading,
  onClose,
  onToggleEnabled,
  onTryInChat,
  onRestoreOriginal,
  onUninstall,
  showToggle = true,
}: SkillDetailDialogProps) {
  const t = useT()
  const [menuPosition, setMenuPosition] = useState<ContextMenuPosition | null>(null)
  const skillDir = dirname(skill.path)
  const displayName = skill.displayName ?? skill.name
  const shortDescription = skill.shortDescription ?? skill.description

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        onClose()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  return (
    <div role="presentation" style={modalScrim} onClick={onClose}>
      <section
        role="dialog"
        aria-modal="true"
        aria-labelledby="skill-detail-title"
        style={modalPanel}
        onClick={(event) => event.stopPropagation()}
      >
        <ActionTooltip label={t('common.close')}>
          <button type="button" aria-label={t('common.close')} style={closeButton} onClick={onClose}>
            <X size={16} strokeWidth={2} />
          </button>
        </ActionTooltip>

        <header style={header}>
          <SkillAvatar
            name={skill.name}
            displayName={displayName}
            iconDataUrl={skill.iconSmallDataUrl ?? skill.iconLargeDataUrl}
            size={44}
          />
          <div style={headerCopy}>
            <div style={titleRow}>
              <h2 id="skill-detail-title" style={title}>
                {displayName}
              </h2>
              {skill.hasVariant ? <VariantBadge /> : null}
            </div>
            <p style={description}>{shortDescription}</p>
          </div>
          <div style={headerActions}>
            {showToggle ? (
              <PillSwitch
                checked={skill.enabled}
                onChange={onToggleEnabled}
                aria-label={t('skillCard.toggleLabel', { name: displayName })}
                size="sm"
              />
            ) : null}
            <ActionTooltip label={t('skillDetail.moreActions')}>
              <button
                type="button"
                aria-label={t('skillDetail.moreActions')}
                style={iconButton}
                onClick={(event) => {
                  const rect = event.currentTarget.getBoundingClientRect()
                  setMenuPosition({ x: rect.right - 160, y: rect.bottom + 6 })
                }}
              >
                <Ellipsis size={16} strokeWidth={2} />
              </button>
            </ActionTooltip>
          </div>
        </header>

        <div style={bodyFrame} data-testid="skill-detail-scroll-body">
          {loading ? (
            <div style={loadingText}>{t('common.loading')}</div>
          ) : (
            <MarkdownRenderer content={markdownBody || skill.description} />
          )}
        </div>

        <footer style={footer}>
          {onUninstall ? (
            <button type="button" style={uninstallButton} onClick={onUninstall}>
              {t('skillDetail.uninstall')}
            </button>
          ) : (
            <span style={statusText}>{skill.enabled ? t('skillCard.on') : t('skillCard.disabledBadge')}</span>
          )}
          <button type="button" style={tryButton} onClick={onTryInChat}>
            <MessageCircle size={15} strokeWidth={2} />
            {t('skillDetail.tryInChat')}
          </button>
        </footer>

        {menuPosition ? (
          <ContextMenu
            position={menuPosition}
            onClose={() => setMenuPosition(null)}
            items={[
              {
                label: t('skillDetail.openFolder'),
                onClick: () => {
                  void window.api.shell.openPath(skillDir)
                },
              },
              {
                label: t('skillDetail.restoreOriginal'),
                onClick: () => {
                  onRestoreOriginal?.()
                },
              },
            ]}
          />
        ) : null}
      </section>
    </div>
  )
}

const modalScrim: React.CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 70,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: '24px',
  background: 'rgba(0, 0, 0, 0.54)',
  backdropFilter: 'blur(3px)',
}

const modalPanel: React.CSSProperties = {
  position: 'relative',
  width: 'min(600px, calc(100vw - 48px))',
  maxHeight: 'min(86vh, 720px)',
  display: 'flex',
  flexDirection: 'column',
  gap: 16,
  padding: '30px 20px 20px',
  borderRadius: 18,
  border: '1px solid var(--border-primary)',
  background: 'var(--bg-secondary)',
  boxShadow: '0 24px 80px rgba(0, 0, 0, 0.48)',
  color: 'var(--text-primary)',
  overflow: 'hidden',
}

const closeButton: React.CSSProperties = {
  position: 'absolute',
  top: 16,
  right: 16,
  width: 28,
  height: 28,
  border: 'none',
  borderRadius: 8,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'transparent',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
}

const header: React.CSSProperties = {
  display: 'grid',
  gridTemplateColumns: '44px minmax(0, 1fr) auto',
  alignItems: 'start',
  gap: 14,
  paddingRight: 26,
}

const headerCopy: React.CSSProperties = {
  minWidth: 0,
  paddingTop: 2,
}

const titleRow: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 8,
  minWidth: 0,
}

const title: React.CSSProperties = {
  margin: 0,
  fontSize: 21,
  lineHeight: 1.25,
  fontWeight: 700,
  color: 'var(--text-primary)',
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap',
}

const description: React.CSSProperties = {
  margin: '8px 0 0',
  fontSize: 14,
  lineHeight: 1.45,
  color: 'var(--text-secondary)',
  overflow: 'hidden',
  display: '-webkit-box',
  WebkitLineClamp: 2,
  WebkitBoxOrient: 'vertical',
}

const headerActions: React.CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 8,
  paddingTop: 24,
}

const iconButton: React.CSSProperties = {
  width: 32,
  height: 32,
  borderRadius: 10,
  border: '1px solid var(--border-primary)',
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'var(--bg-tertiary)',
  color: 'var(--text-secondary)',
  cursor: 'pointer',
}

const bodyFrame: React.CSSProperties = {
  minHeight: 260,
  maxHeight: 'min(54vh, 490px)',
  overflow: 'auto',
  padding: '16px 18px',
  borderRadius: 12,
  border: '1px solid var(--border-secondary)',
  background: 'var(--bg-primary)',
}

const loadingText: React.CSSProperties = {
  color: 'var(--text-tertiary)',
  fontSize: 13,
}

const footer: React.CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 12,
}

const statusText: React.CSSProperties = {
  minWidth: 0,
  fontSize: 13,
  color: 'var(--text-tertiary)',
}

const tryButton: React.CSSProperties = {
  height: 32,
  padding: '0 12px',
  border: 'none',
  borderRadius: 10,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  gap: 7,
  background: 'var(--button-secondary-bg)',
  color: 'var(--text-primary)',
  fontSize: 13,
  cursor: 'pointer',
  whiteSpace: 'nowrap',
}

const uninstallButton: React.CSSProperties = {
  height: 32,
  padding: '0 12px',
  border: 'none',
  borderRadius: 10,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  background: 'color-mix(in srgb, var(--error) 16%, transparent)',
  color: 'var(--error)',
  fontSize: 13,
  fontWeight: 600,
  cursor: 'pointer',
  whiteSpace: 'nowrap',
}

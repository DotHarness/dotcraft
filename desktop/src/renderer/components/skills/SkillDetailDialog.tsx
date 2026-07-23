import { useEffect, useState } from 'react'
import { Ellipsis, MessageCircle } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { useConversationStore } from '../../stores/conversationStore'
import type { SkillEntry } from '../../stores/skillsStore'
import { dirname } from '../../utils/path'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { ModalHeader } from '../ui/ModalHeader'
import { SkillAvatar } from './SkillAvatar'
import { VariantBadge } from './VariantBadge'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'

interface SkillDetailDialogProps {
  skill: SkillEntry
  markdownBody: string
  loading: boolean
  onClose: () => void
  onTryInChat: () => void
  onRestoreOriginal?: () => void
  onUninstall?: () => void
}

export function SkillDetailDialog({
  skill,
  markdownBody,
  loading,
  onClose,
  onTryInChat,
  onRestoreOriginal,
  onUninstall,
}: SkillDetailDialogProps) {
  const t = useT()
  const remoteWorkspaceActive = useConversationStore((s) => s.remoteWorkspaceActive)
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
        {/* The skill's own artwork is already a badge, so it replaces the neutral
            one rather than nesting inside it. See DESIGN.md Dialog Headers. */}
        <ModalHeader
          icon={
            <SkillAvatar
              name={skill.name}
              displayName={displayName}
              iconDataUrl={skill.iconSmallDataUrl ?? skill.iconLargeDataUrl}
              size={36}
            />
          }
          badgedIcon={false}
          title={displayName}
          titleAdornment={skill.hasVariant ? <VariantBadge /> : null}
          titleId="skill-detail-title"
          description={shortDescription}
          onClose={onClose}
          closeLabel={t('common.close')}
          actions={
            <IconButton
              icon={<Ellipsis size={16} aria-hidden />}
              label={t('skillDetail.moreActions')}
              tooltipLabel={t('skillDetail.moreActions')}
              size={30}
              aria-haspopup="menu"
              aria-expanded={menuPosition != null}
              onClick={(event) => {
                const rect = event.currentTarget.getBoundingClientRect()
                setMenuPosition({ x: rect.right - 160, y: rect.bottom + 6 })
              }}
            />
          }
          style={{ marginBottom: 0 }}
        />

        <div style={bodyFrame} data-testid="skill-detail-scroll-body">
          {loading ? (
            <div style={loadingText}>{t('common.loading')}</div>
          ) : (
            <MarkdownRenderer content={markdownBody || skill.description} />
          )}
        </div>

        {/* Enabling a skill lives in the manage list, so the preview carries none of it. */}
        <footer style={footerStyle(onUninstall != null)}>
          {onUninstall && (
            <Button type="button" variant="danger" onClick={onUninstall}>
              {t('skillDetail.uninstall')}
            </Button>
          )}
          <Button type="button" variant="primary" onClick={onTryInChat}>
            <MessageCircle size={15} strokeWidth={2} />
            {t('skillDetail.tryInChat')}
          </Button>
        </footer>

        {menuPosition ? (
          <ContextMenu
            position={menuPosition}
            onClose={() => setMenuPosition(null)}
            items={[
              {
                label: t('skillDetail.openFolder'),
                disabled: remoteWorkspaceActive,
                title: remoteWorkspaceActive ? t('skillDetail.openFolderRemoteUnavailable') : undefined,
                onClick: () => {
                  if (remoteWorkspaceActive) {
                    addToast(t('skillDetail.openFolderRemoteUnavailable'), 'warning')
                    return
                  }
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
  padding: '20px',
  borderRadius: 18,
  border: '1px solid transparent',
  background: 'var(--bg-secondary)',
  boxShadow: '0 24px 80px rgba(0, 0, 0, 0.48)',
  color: 'var(--text-primary)',
  overflow: 'hidden',
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

// Without a destructive action on the left there is nothing to space apart, so the
// single action sits where every dialog's principal action sits.
function footerStyle(hasLeadingAction: boolean): React.CSSProperties {
  return {
    display: 'flex',
    alignItems: 'center',
    justifyContent: hasLeadingAction ? 'space-between' : 'flex-end',
    gap: 12,
  }
}

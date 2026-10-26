import { useEffect, useId, useState } from 'react'
import { MessageCircle } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { useConversationStore } from '../../stores/conversationStore'
import type { SkillEntry } from '../../stores/skillsStore'
import { dirname } from '../../utils/path'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { ContextMenu, type ContextMenuPosition } from '../ui/ContextMenu'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import { SkillAvatar } from './SkillAvatar'
import { Button } from '../ui/Button'
import { MoreActionsButton } from '../ui/MoreActionsButton'
import styles from './SkillDetailDialog.module.css'

interface SkillDetailDialogProps {
  skill: SkillEntry
  markdownBody: string
  loading: boolean
  error?: boolean
  previewOnly?: boolean
  onClose: () => void
  onTryInChat?: () => void
  onUninstall?: () => void
}

export function SkillDetailDialog({
  skill,
  markdownBody,
  loading,
  error = false,
  previewOnly = false,
  onClose,
  onTryInChat,
  onUninstall,
}: SkillDetailDialogProps) {
  const t = useT()
  const titleId = useId()
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
    <ModalDialog
      titleId={titleId}
      width={600}
      onDismiss={onClose}
      header={(
        <ModalHeader
          icon={
            <SkillAvatar iconDataUrl={skill.iconSmallDataUrl ?? skill.iconLargeDataUrl} size={36} />
          }
          badgedIcon={false}
          title={displayName}
          titleId={titleId}
          description={shortDescription}
          onClose={onClose}
          closeLabel={t('common.close')}
          actions={!previewOnly ? (
            <MoreActionsButton
              label={t('skillDetail.moreActions')}
              size={30}
              open={menuPosition != null}
              onClick={(event) => {
                const rect = event.currentTarget.getBoundingClientRect()
                setMenuPosition({ x: rect.right - 160, y: rect.bottom + 6 })
              }}
            />
          ) : undefined}
        />
      )}
      footer={(
        <ModalFooter
          leading={!previewOnly && onUninstall ? (
            <Button type="button" variant="danger" onClick={onUninstall}>
              {t('skillDetail.uninstall')}
            </Button>
          ) : undefined}
        >
          {previewOnly ? (
            <Button type="button" variant="primary" onClick={onClose}>
              {t('common.close')}
            </Button>
          ) : (
            <Button type="button" variant="primary" onClick={onTryInChat}>
              <MessageCircle size={15} strokeWidth={2} />
              {t('skillDetail.tryInChat')}
            </Button>
          )}
        </ModalFooter>
      )}
    >
      <div className={styles.document} data-testid="skill-detail-scroll-body">
        {loading ? (
          <div className={styles.status}>{t('common.loading')}</div>
        ) : error ? (
          <div className={styles.status}>{t('skillDetail.loadFailed')}</div>
        ) : (
          <MarkdownRenderer content={markdownBody || skill.description} />
        )}
      </div>

      {!previewOnly && menuPosition ? (
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
          ]}
        />
      ) : null}
    </ModalDialog>
  )
}

import { useT } from '../../../contexts/LocaleContext'
import { useConversationStore } from '../../../stores/conversationStore'
import { OpenTargetButton } from '../../conversation/OpenTargetButton'
import { Button } from '../../ui/Button'
import { FileTypeIcon } from '../../ui/FileTypeIcon'
import { fileTypeLabel, formatPreviewSize, type PreviewUnavailableReason } from './filePreview'
import styles from './ViewerNotice.module.css'

interface UnavailableNoticeProps {
  absolutePath: string
  relativePath: string
  reason: PreviewUnavailableReason
}

export function UnavailableNotice({ absolutePath, relativePath, reason }: UnavailableNoticeProps): JSX.Element {
  const t = useT()
  const projectRoot = useConversationStore((s) => s.workspacePath)
  const type = fileTypeLabel(relativePath)
  const [title, detail] = reason.kind === 'size'
    ? [t('viewer.unavailable.sizeTitle'), t('viewer.unavailable.sizeReason', {
        size: formatPreviewSize(reason.sizeBytes),
        limit: formatPreviewSize(reason.limitBytes)
      })]
    : reason.kind === 'load'
      ? [t('viewer.unavailable.loadTitle'), t('viewer.unavailable.loadReason')]
      : [type ? t('viewer.unavailable.typeTitle', { type }) : t('viewer.unavailable.typeTitleGeneric'), t('viewer.unavailable.typeReason')]

  return (
    <div className={styles.unavailable}>
      <div className={styles.unavailableBody} role="status">
        <FileTypeIcon path={relativePath} size={24} />
        <div className={styles.unavailableText}>
          <strong className={styles.unavailableTitle}>{title}</strong>
          <span className={styles.unavailableReason}>{detail}</span>
        </div>
        <div className={styles.unavailableActions}>
          {reason.kind === 'load' && (
            <Button size="sm" variant="outline" onClick={reason.onRetry}>{t('viewer.unavailable.retry')}</Button>
          )}
          <OpenTargetButton
            targetPath={absolutePath}
            projectRoot={projectRoot}
            rememberChoice
            tooltipLabel={t('detailPanel.openFileTitle', { path: relativePath })}
            menuAriaLabel={t('detailPanel.openFileMenuAria')}
            showPrimaryLabel
            variant="outline"
          />
        </div>
      </div>
    </div>
  )
}

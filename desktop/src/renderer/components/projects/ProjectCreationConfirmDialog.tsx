import { useEffect, useRef, type JSX } from 'react'
import { Folder, FolderOpen, FolderPlus } from 'lucide-react'
import type { ProjectCreationConfirmDetails } from '../../../shared/desktopToolConfirm'
import { useT } from '../../contexts/LocaleContext'
import { basename } from '../../utils/path'
import { Button } from '../ui/Button'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import projectStyles from './ProjectDialog.module.css'
import styles from './ProjectCreationConfirmDialog.module.css'

export function ProjectCreationConfirmDialog({
  request,
  submitting = false,
  onRespond
}: {
  request: ProjectCreationConfirmDetails
  submitting?: boolean
  onRespond: (confirmed: boolean) => void
}): JSX.Element {
  const t = useT()
  const confirmRef = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    confirmRef.current?.focus()
    const onKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') onRespond(false)
    }
    document.addEventListener('keydown', onKeyDown)
    return () => document.removeEventListener('keydown', onKeyDown)
  }, [onRespond])

  const multiFolder = request.folders.length > 1
  const description = request.createsNewFolder
    ? t('addProject.confirmAgentNewDescription')
    : t('addProject.confirmAgentAttachDescription')

  return (
    <ModalDialog
      ariaLabel={t('addProject.confirmAgentTitle')}
      width={460}
      onDismiss={() => onRespond(false)}
      bodyClassName={projectStyles.body}
      header={(
        <ModalHeader
          icon={<FolderPlus size={18} aria-hidden />}
          title={t('addProject.confirmAgentTitle')}
          description={description}
          onClose={() => onRespond(false)}
          closeLabel={t('addProject.close')}
        />
      )}
      footer={(
        <ModalFooter>
          <Button variant="secondary" disabled={submitting} onClick={() => onRespond(false)}>
            {t('addProject.cancel')}
          </Button>
          <Button ref={confirmRef} variant="primary" loading={submitting} onClick={() => onRespond(true)}>
            {t('addProject.create')}
          </Button>
        </ModalFooter>
      )}
    >
      <div className={projectStyles.field}>
        <div className={projectStyles.label}>{t('addProject.nameLabel')}</div>
        <div className={styles.nameValue}>{request.name}</div>
      </div>

      <div className={projectStyles.field}>
        <div className={projectStyles.label}>{t('addProject.sourceFolders')}</div>
        <div className={projectStyles.folders}>
          {request.folders.map((folderPath, index) => (
            <div key={folderPath} className={projectStyles.folder}>
              <span className={projectStyles.folderIcon}>
                {index === 0
                  ? <FolderOpen size={16} strokeWidth={1.7} aria-hidden />
                  : <Folder size={16} strokeWidth={1.7} aria-hidden />}
              </span>
              <span className={projectStyles.folderText}>
                <span className={projectStyles.folderName}>{basename(folderPath)}</span>
                <span className={projectStyles.folderPath} title={folderPath}>
                  {folderPath}
                </span>
              </span>
              <span className={projectStyles.folderActions}>
                {index === 0 && multiFolder && (
                  <span className={projectStyles.primaryBadge}>{t('addProject.primary')}</span>
                )}
              </span>
            </div>
          ))}
        </div>
        {request.createsNewFolder && (
          <p className={projectStyles.hint}>{t('addProject.confirmAgentNewFolderHint')}</p>
        )}
      </div>
    </ModalDialog>
  )
}

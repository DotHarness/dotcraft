import { useEffect, useId, useMemo, type MouseEvent as ReactMouseEvent } from 'react'
import { AlertCircle, CheckCircle2, Download, ExternalLink } from 'lucide-react'
import DOMPurify from 'dompurify'

import type { AppUpdateState } from '../../../shared/appUpdate'
import { useT } from '../../contexts/LocaleContext'
import { Button } from '../ui/Button'
import { ModalDialog, ModalFooter } from '../ui/ModalDialog'
import { ModalHeader } from '../ui/ModalHeader'
import styles from './AppUpdateDialog.module.css'

interface AppUpdateDialogProps {
  state: AppUpdateState
  onClose: () => void
  onDownload: () => void
  onInstall: () => void
}

export function AppUpdateDialog({
  state,
  onClose,
  onDownload,
  onInstall
}: AppUpdateDialogProps): JSX.Element {
  const t = useT()
  const titleId = useId()
  const update = state.update
  const downloading = state.status === 'downloading'
  const downloaded = state.status === 'downloaded'
  const failed = state.status === 'error'
  const error = failed ? state.error : undefined
  const progress = state.progress
  const releaseNotesHtml = useMemo(
    () => (update?.releaseNotes ? DOMPurify.sanitize(update.releaseNotes) : ''),
    [update?.releaseNotes]
  )

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape') {
        event.preventDefault()
        onClose()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [onClose])

  const primaryLabel = downloaded
    ? t('update.restart')
    : downloading
      ? t('update.downloading')
      : failed
        ? t('update.retry')
        : t('update.download')

  return (
    <ModalDialog
      titleId={titleId}
      width={620}
      maxHeight={720}
      onDismiss={onClose}
      header={(
        <ModalHeader
          icon={downloaded ? <CheckCircle2 size={18} aria-hidden /> : <Download size={18} aria-hidden />}
          title={t('update.title')}
          titleId={titleId}
          description={
            update ? t('update.subtitle', { version: update.latestVersion }) : t('update.checking')
          }
          onClose={onClose}
          closeLabel={t('update.closeAria')}
        />
      )}
      error={error ? (
        <span className={styles.error}>
          <AlertCircle size={16} strokeWidth={2} aria-hidden="true" />
          <span>{error}</span>
        </span>
      ) : null}
      footer={(
        <ModalFooter
          leading={update?.htmlUrl ? (
            <Button
              variant="ghost"
              iconLeft={<ExternalLink size={14} strokeWidth={2} aria-hidden="true" />}
              onClick={() => {
                void window.api.shell.openExternal(update.htmlUrl)
              }}
            >
              {t('update.viewRelease')}
            </Button>
          ) : undefined}
        >
          {failed && (
            <Button variant="secondary" onClick={onClose}>
              {t('update.cancel')}
            </Button>
          )}
          <Button
            variant="primary"
            onClick={downloaded ? onInstall : onDownload}
            disabled={!update || downloading}
            loading={downloading}
          >
            {primaryLabel}
          </Button>
        </ModalFooter>
      )}
    >
      {update ? (
        <div className={`app-update-notes ${styles.notes}`}>
          {releaseNotesHtml ? (
            <div
              className="markdown-body markdown-body--contained"
              onClick={openReleaseNotesLink}
              dangerouslySetInnerHTML={{ __html: releaseNotesHtml }}
            />
          ) : (
            <div className="markdown-body markdown-body--contained">
              <p>{t('update.noReleaseNotes')}</p>
            </div>
          )}
        </div>
      ) : (
        <p className={styles.checking}>{t('update.checkingBody')}</p>
      )}

      {downloading && (
        <div className={styles.progress}>
          <div className={styles.progressLabel}>
            <span>{t('update.downloading')}</span>
            <span className={styles.progressBytes}>
              {formatBytes(progress?.transferredBytes ?? 0)} / {formatBytes(progress?.totalBytes ?? 0)}
              {' · '}
              {Math.round(progress?.percent ?? 0)}%
            </span>
          </div>
          <div className={styles.progressTrack}>
            <div
              className={styles.progressFill}
              style={{ width: `${Math.max(0, Math.min(100, progress?.percent ?? 0))}%` }}
            />
          </div>
        </div>
      )}

      {downloaded && (
        <div className={styles.success}>
          <CheckCircle2 size={16} strokeWidth={2} aria-hidden="true" />
          {t('update.readyToRestart')}
        </div>
      )}
    </ModalDialog>
  )
}

function openReleaseNotesLink(event: ReactMouseEvent<HTMLDivElement>): void {
  const anchor = (event.target as HTMLElement).closest('a[href]')
  if (!anchor) return
  event.preventDefault()
  const href = anchor.getAttribute('href') ?? ''
  if (/^https?:\/\//i.test(href)) void window.api.shell.openExternal(href)
}

function formatBytes(bytes: number): string {
  if (!Number.isFinite(bytes) || bytes <= 0) return '0 B'
  const units = ['B', 'KB', 'MB', 'GB']
  let value = bytes
  let unitIndex = 0
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex += 1
  }
  return `${value >= 10 || unitIndex === 0 ? Math.round(value) : value.toFixed(1)} ${units[unitIndex]}`
}

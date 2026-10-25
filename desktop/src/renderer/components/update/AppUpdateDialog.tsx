import { useEffect, useId, useMemo, type CSSProperties, type MouseEvent as ReactMouseEvent } from 'react'
import { AlertCircle, CheckCircle2, Download, ExternalLink } from 'lucide-react'
import DOMPurify from 'dompurify'

import type { AppUpdateProgress, AppUpdateState } from '../../../shared/appUpdate'
import { useT } from '../../contexts/LocaleContext'
import { ActionTooltip } from '../ui/ActionTooltip'
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
    : failed
      ? t('update.retry')
      : t('update.download')

  return (
    <ModalDialog
      titleId={titleId}
      width={620}
      maxHeight={720}
      onDismiss={onClose}
      bodyClassName={styles.body}
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
            <button
              type="button"
              className={`dc-quiet-action ${styles.release}`}
              onClick={() => {
                void window.api.shell.openExternal(update.htmlUrl)
              }}
            >
              <ExternalLink size={14} strokeWidth={2} aria-hidden="true" />
              {t('update.viewRelease')}
            </button>
          ) : undefined}
        >
          {failed && (
            <Button variant="secondary" onClick={onClose}>
              {t('update.cancel')}
            </Button>
          )}
          {downloading ? (
            <DownloadProgress progress={progress} />
          ) : (
            <Button
              variant="primary"
              className={styles.primary}
              onClick={downloaded ? onInstall : onDownload}
              disabled={!update}
            >
              {primaryLabel}
            </Button>
          )}
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
    </ModalDialog>
  )
}

function DownloadProgress({ progress }: { progress?: AppUpdateProgress }): JSX.Element {
  const t = useT()
  const percent = Math.round(Math.max(0, Math.min(100, progress?.percent ?? 0)))
  const label = t('update.downloadingPercent', { percent })
  const detail = progress && progress.bytesPerSecond > 0
    ? t('update.transferDetail', {
        transferred: formatBytes(progress.transferredBytes),
        total: formatBytes(progress.totalBytes),
        speed: formatBytes(progress.bytesPerSecond)
      })
    : ''
  return (
    <ActionTooltip label={detail}>
      <span
        className={`${styles.primary} ${styles.progress}`}
        role="progressbar"
        tabIndex={0}
        aria-label={t('update.downloadProgressAria')}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-valuenow={percent}
        style={{ '--progress': `${percent}%` } as CSSProperties}
      >
        <span className={styles.progressLabel}><span className="dc-button__label">{label}</span></span>
        <span className={styles.progressFill} aria-hidden="true">
          <span className={styles.progressLabel}><span className="dc-button__label">{label}</span></span>
        </span>
      </span>
    </ActionTooltip>
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

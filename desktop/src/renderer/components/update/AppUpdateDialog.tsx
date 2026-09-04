import { useEffect, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { AlertCircle, CheckCircle2, Download, ExternalLink } from 'lucide-react'

import type { AppUpdateState } from '../../../shared/appUpdate'
import { useT } from '../../contexts/LocaleContext'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { Button } from '../ui/Button'
import { ModalHeader } from '../ui/ModalHeader'

interface AppUpdateDialogProps {
  state: AppUpdateState
  onClose: () => void
  onDownload: () => void
}

export function AppUpdateDialog({
  state,
  onClose,
  onDownload
}: AppUpdateDialogProps): JSX.Element {
  const t = useT()
  const update = state.update
  const downloading = state.status === 'downloading'
  const downloaded = state.status === 'downloaded'
  const failed = state.status === 'error'
  const error = failed ? state.error : undefined
  const canClose = !downloading && !downloaded
  const progress = state.progress

  useEffect(() => {
    const handleKeyDown = (event: KeyboardEvent): void => {
      if (event.key === 'Escape' && canClose) {
        event.preventDefault()
        onClose()
      }
    }
    window.addEventListener('keydown', handleKeyDown)
    return () => window.removeEventListener('keydown', handleKeyDown)
  }, [canClose, onClose])

  const primaryLabel = downloading
    ? t('update.downloading')
    : downloaded
      ? t('update.installing')
      : failed
        ? t('update.retry')
        : t('update.download')

  const dialog = (
    <div
      style={backdropStyle}
      onMouseDown={(event) => {
        if (event.target === event.currentTarget && canClose) {
          onClose()
        }
      }}
    >
      <section
        role="dialog"
        aria-modal="true"
        aria-labelledby="app-update-title"
        style={dialogStyle}
      >
        <ModalHeader
          icon={downloaded ? <CheckCircle2 size={18} aria-hidden /> : <Download size={18} aria-hidden />}
          title={t('update.title')}
          titleId="app-update-title"
          description={
            update ? t('update.subtitle', { version: update.latestVersion }) : t('update.checking')
          }
          onClose={canClose ? onClose : undefined}
          closeLabel={t('update.closeAria')}
          style={headerStyle}
        />

        <div style={contentStyle}>
          {update ? (
            <div className="app-update-notes" style={notesStyle}>
              <MarkdownRenderer
                content={update.releaseNotes || t('update.noReleaseNotes')}
                linkMode="external"
                containOverflow
              />
            </div>
          ) : (
            <p style={bodyStyle}>{t('update.checkingBody')}</p>
          )}

          {(downloading || downloaded) && (
            <div style={progressWrapStyle}>
              <div style={progressLabelStyle}>
                <span>{downloaded ? t('update.installing') : t('update.downloading')}</span>
                <span style={progressBytesStyle}>
                  {formatBytes(progress?.transferredBytes ?? 0)} / {formatBytes(progress?.totalBytes ?? update?.sizeBytes ?? 0)}
                  {' · '}
                  {Math.round(progress?.percent ?? 0)}%
                </span>
              </div>
              <div style={progressTrackStyle}>
                <div
                  style={{
                    ...progressFillStyle,
                    width: `${Math.max(0, Math.min(100, progress?.percent ?? 0))}%`
                  }}
                />
              </div>
            </div>
          )}

          {downloaded && (
            <div style={successStyle}>
              <CheckCircle2 size={16} strokeWidth={2} aria-hidden="true" />
              {t('update.installSoon')}
            </div>
          )}

          {error && (
            <div style={errorStyle}>
              <AlertCircle size={16} strokeWidth={2} aria-hidden="true" />
              <span>{error}</span>
            </div>
          )}
        </div>

        <footer style={footerStyle}>
          <div style={footerLeadStyle}>
            {update?.htmlUrl && (
              <Button
                variant="ghost"
                iconLeft={<ExternalLink size={14} strokeWidth={2} aria-hidden="true" />}
                onClick={() => {
                  void window.api.shell.openExternal(update.htmlUrl as string)
                }}
              >
                {t('update.viewRelease')}
              </Button>
            )}
          </div>
          <div style={footerActionsStyle}>
            {failed && (
              <Button variant="secondary" onClick={onClose}>
                {t('update.cancel')}
              </Button>
            )}
            <Button
              variant="primary"
              onClick={onDownload}
              disabled={!update || downloading || downloaded}
              loading={downloading}
            >
              {primaryLabel}
            </Button>
          </div>
        </footer>
      </section>
    </div>
  )

  return createPortal(dialog, document.body)
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

const backdropStyle: CSSProperties = {
  position: 'fixed',
  inset: 0,
  zIndex: 2100,
  display: 'flex',
  alignItems: 'center',
  justifyContent: 'center',
  padding: 16,
  background: 'var(--overlay-scrim)'
}

const dialogStyle: CSSProperties = {
  position: 'relative',
  width: 'min(620px, calc(100vw - 32px))',
  maxHeight: 'min(720px, calc(100vh - 32px))',
  display: 'flex',
  flexDirection: 'column',
  overflow: 'hidden',
  border: '1px solid var(--border-subtle)',
  borderRadius: 8,
  background: 'var(--bg-primary)',
  color: 'var(--text-primary)',
  boxShadow: '0 24px 80px rgba(0, 0, 0, 0.38)'
}

const headerStyle: CSSProperties = {
  margin: '20px 22px 0'
}

const contentStyle: CSSProperties = {
  display: 'flex',
  minHeight: 0,
  flexDirection: 'column',
  padding: '0 22px'
}

const bodyStyle: CSSProperties = {
  margin: 0,
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-body-size)',
  lineHeight: 'var(--type-body-line-height)'
}

const notesStyle: CSSProperties = {
  minHeight: 120,
  maxHeight: 360,
  overflowY: 'auto',
  padding: '12px 14px',
  border: '1px solid var(--border-subtle)',
  borderRadius: 8,
  background: 'var(--bg-secondary)',
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-prose-line-height)',
  overflowWrap: 'anywhere'
}

const progressWrapStyle: CSSProperties = {
  flex: '0 0 auto',
  marginTop: 16
}

const progressLabelStyle: CSSProperties = {
  display: 'flex',
  justifyContent: 'space-between',
  gap: 12,
  marginBottom: 7,
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
}

const progressBytesStyle: CSSProperties = {
  flex: '0 0 auto',
  fontVariantNumeric: 'tabular-nums'
}

const progressTrackStyle: CSSProperties = {
  height: 8,
  overflow: 'hidden',
  borderRadius: 999,
  background: 'var(--bg-tertiary)'
}

const progressFillStyle: CSSProperties = {
  height: '100%',
  borderRadius: 999,
  background: 'var(--accent)',
  transition: 'width 120ms ease'
}

const successStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 7,
  marginTop: 14,
  color: 'var(--success)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
}

const errorStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'flex-start',
  gap: 7,
  marginTop: 14,
  color: 'var(--error)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  overflowWrap: 'anywhere'
}

const footerStyle: CSSProperties = {
  display: 'flex',
  flex: '0 0 auto',
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 12,
  padding: '18px 22px 20px'
}

const footerLeadStyle: CSSProperties = {
  display: 'flex',
  minWidth: 0,
  marginLeft: -10
}

const footerActionsStyle: CSSProperties = {
  display: 'flex',
  flex: '0 0 auto',
  gap: 8
}

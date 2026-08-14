import { useEffect, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { AlertCircle, CheckCircle2, Download, ExternalLink, LoaderCircle, X } from 'lucide-react'

import type { AppUpdateState } from '../../../shared/appUpdate'
import { useT } from '../../contexts/LocaleContext'
import { MarkdownRenderer } from '../conversation/MarkdownRenderer'
import { Button } from '../ui/Button'
import { IconButton } from '../ui/IconButton'

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
  const error = state.status === 'error' ? state.error : undefined
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
        <div style={closeStyle}>
          <IconButton
            label={t('update.closeAria')}
            icon={<X size={18} strokeWidth={2} aria-hidden="true" />}
            onClick={onClose}
            disabled={!canClose}
          />
        </div>

        <header style={headerStyle}>
          <div style={eyebrowStyle}>
            {downloaded ? (
              <CheckCircle2 size={15} strokeWidth={2} aria-hidden="true" />
            ) : downloading ? (
              <LoaderCircle size={15} strokeWidth={2} aria-hidden="true" style={spinStyle} />
            ) : (
              <Download size={15} strokeWidth={2} aria-hidden="true" />
            )}
            <span>
              {update
                ? t('update.subtitle', { version: update.latestVersion })
                : t('update.checking')}
            </span>
          </div>
        </header>

        <h2 id="app-update-title" style={titleStyle}>
          {t('update.title')}
        </h2>

        <div style={contentStyle}>
          {update ? (
            <div style={releaseNotesStyle}>
              <div style={releaseNotesHeaderStyle}>
                <div style={releaseNotesTitleStyle}>{t('update.releaseNotes')}</div>
                {update.htmlUrl && (
                  <Button
                    variant="ghost"
                    size="sm"
                    style={releaseNotesActionStyle}
                    iconLeft={<ExternalLink size={13} strokeWidth={2} aria-hidden="true" />}
                    onClick={() => {
                      void window.api.shell.openExternal(update.htmlUrl as string)
                    }}
                  >
                    {t('update.viewRelease')}
                  </Button>
                )}
              </div>
              <div style={releaseNotesBodyStyle}>
                <MarkdownRenderer
                  content={update.releaseNotes || t('update.noReleaseNotes')}
                  linkMode="external"
                  containOverflow
                />
              </div>
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
          <Button
            variant="secondary"
            onClick={onClose}
            disabled={!canClose}
          >
            {t('update.cancel')}
          </Button>
          <Button
            variant="primary"
            onClick={onDownload}
            disabled={!update || downloading || downloaded}
            loading={downloading}
          >
            {downloading
              ? t('update.downloading')
              : state.status === 'error'
                ? t('update.retry')
                : t('update.download')}
          </Button>
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

/**
 * The close control sits outside the header flow so its 32px footprint cannot
 * set the header height. Its 18px glyph then lands on the eyebrow's line and on
 * the dialog's 22px inset.
 */
const closeStyle: CSSProperties = {
  position: 'absolute',
  top: 12,
  right: 15,
  zIndex: 1
}

// No rule under the header: the gap and the title weight already mark the edge.
const headerStyle: CSSProperties = {
  display: 'flex',
  alignItems: 'center',
  gap: 16,
  padding: '20px 60px 0 22px'
}

const eyebrowStyle: CSSProperties = {
  display: 'inline-flex',
  minWidth: 0,
  alignItems: 'center',
  gap: 7,
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
}

const titleStyle: CSSProperties = {
  margin: '6px 22px 0',
  fontSize: 22,
  lineHeight: '30px',
  fontWeight: 680
}

const contentStyle: CSSProperties = {
  padding: '16px 22px 0',
  overflowY: 'auto'
}

const bodyStyle: CSSProperties = {
  margin: 0,
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-body-size)',
  lineHeight: 'var(--type-body-line-height)'
}

const releaseNotesStyle: CSSProperties = {
  border: '1px solid var(--border-subtle)',
  borderRadius: 8,
  background: 'var(--bg-secondary)',
  overflow: 'hidden'
}

/**
 * View release belongs beside the content it opens. The rule below the row
 * stays: it marks where the scrolling region begins, not where a section ends.
 */
const releaseNotesHeaderStyle: CSSProperties = {
  display: 'flex',
  minHeight: 36,
  alignItems: 'center',
  justifyContent: 'space-between',
  gap: 12,
  padding: '0 6px 0 11px',
  borderBottom: '1px solid var(--border-subtle)'
}

const releaseNotesTitleStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  color: 'var(--text-primary)',
  fontSize: 'var(--type-ui-size)',
  lineHeight: 'var(--type-ui-line-height)',
  fontWeight: 600,
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

// A longer localized label must not squeeze the action out of the row.
const releaseNotesActionStyle: CSSProperties = {
  flex: '0 0 auto'
}

const releaseNotesBodyStyle: CSSProperties = {
  maxHeight: 156,
  overflowY: 'auto',
  padding: '10px 11px',
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  overflowWrap: 'anywhere'
}

const progressWrapStyle: CSSProperties = {
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

// Transferred bytes ride with the status label instead of a third, dimmer line.
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

// No rule above the footer either; the gap carries the separation.
const footerStyle: CSSProperties = {
  display: 'flex',
  justifyContent: 'flex-end',
  gap: 8,
  padding: '18px 22px 20px'
}

const spinStyle: CSSProperties = {
  animation: 'spin 1s linear infinite'
}

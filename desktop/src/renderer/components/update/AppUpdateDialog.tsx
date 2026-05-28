import { useEffect, useRef, type CSSProperties } from 'react'
import { createPortal } from 'react-dom'
import { AlertCircle, CheckCircle2, Download, ExternalLink, LoaderCircle, X } from 'lucide-react'

import type { AppUpdateState } from '../../../shared/appUpdate'
import { useT } from '../../contexts/LocaleContext'

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
  const closeButtonRef = useRef<HTMLButtonElement | null>(null)
  const update = state.update
  const downloading = state.status === 'downloading'
  const downloaded = state.status === 'downloaded'
  const error = state.status === 'error' ? state.error : undefined
  const canClose = !downloading && !downloaded
  const progress = state.progress

  useEffect(() => {
    closeButtonRef.current?.focus()
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
        <header style={headerStyle}>
          <div style={{ minWidth: 0 }}>
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
            <h2 id="app-update-title" style={titleStyle}>
              {t('update.title')}
            </h2>
          </div>
          <button
            ref={closeButtonRef}
            type="button"
            aria-label={t('update.closeAria')}
            onClick={onClose}
            disabled={!canClose}
            style={{
              ...iconButtonStyle,
              opacity: canClose ? 1 : 0.5,
              cursor: canClose ? 'pointer' : 'default'
            }}
          >
            <X size={18} strokeWidth={2} aria-hidden="true" />
          </button>
        </header>

        <div style={contentStyle}>
          {update ? (
            <>
              <p style={bodyStyle}>
                {t('update.body', {
                  current: update.currentVersion,
                  next: update.latestVersion
                })}
              </p>

              <div style={metaGridStyle}>
                <UpdateMeta label={t('update.currentVersion')} value={update.currentVersion} />
                <UpdateMeta label={t('update.latestVersion')} value={update.latestVersion} />
                <UpdateMeta label={t('update.asset')} value={update.assetName} />
                <UpdateMeta label={t('update.size')} value={formatBytes(update.sizeBytes)} />
              </div>

              <div style={releaseNotesStyle}>
                <div style={releaseNotesTitleStyle}>{t('update.releaseNotes')}</div>
                <div style={releaseNotesBodyStyle}>
                  {update.releaseNotes || t('update.noReleaseNotes')}
                </div>
              </div>

              {update.htmlUrl && (
                <button
                  type="button"
                  onClick={() => {
                    void window.api.shell.openExternal(update.htmlUrl as string)
                  }}
                  style={linkButtonStyle}
                >
                  <ExternalLink size={14} strokeWidth={2} aria-hidden="true" />
                  {t('update.viewRelease')}
                </button>
              )}
            </>
          ) : (
            <p style={bodyStyle}>{t('update.checkingBody')}</p>
          )}

          {(downloading || downloaded) && (
            <div style={progressWrapStyle}>
              <div style={progressLabelStyle}>
                <span>{downloaded ? t('update.installing') : t('update.downloading')}</span>
                <span>{Math.round(progress?.percent ?? 0)}%</span>
              </div>
              <div style={progressTrackStyle}>
                <div
                  style={{
                    ...progressFillStyle,
                    width: `${Math.max(0, Math.min(100, progress?.percent ?? 0))}%`
                  }}
                />
              </div>
              <div style={progressBytesStyle}>
                {formatBytes(progress?.transferredBytes ?? 0)} / {formatBytes(progress?.totalBytes ?? update?.sizeBytes ?? 0)}
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
          <button
            type="button"
            onClick={onClose}
            disabled={!canClose}
            style={secondaryButtonStyle(!canClose)}
          >
            {t('update.cancel')}
          </button>
          <button
            type="button"
            onClick={onDownload}
            disabled={!update || downloading || downloaded}
            style={primaryButtonStyle(!update || downloading || downloaded)}
          >
            {downloading
              ? t('update.downloading')
              : state.status === 'error'
                ? t('update.retry')
                : t('update.download')}
          </button>
        </footer>
      </section>
    </div>
  )

  return createPortal(dialog, document.body)
}

function UpdateMeta({ label, value }: { label: string; value: string }): JSX.Element {
  return (
    <div style={metaItemStyle}>
      <span style={metaLabelStyle}>{label}</span>
      <span style={metaValueStyle}>{value}</span>
    </div>
  )
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
  background: 'rgba(6, 10, 18, 0.62)',
  backdropFilter: 'blur(10px)'
}

const dialogStyle: CSSProperties = {
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
  display: 'flex',
  alignItems: 'flex-start',
  justifyContent: 'space-between',
  gap: 16,
  padding: '20px 22px 14px',
  borderBottom: '1px solid var(--border-subtle)'
}

const eyebrowStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 7,
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)'
}

const titleStyle: CSSProperties = {
  margin: '6px 0 0',
  fontSize: 22,
  lineHeight: '30px',
  fontWeight: 680
}

const iconButtonStyle: CSSProperties = {
  width: 32,
  height: 32,
  flexShrink: 0,
  display: 'inline-flex',
  alignItems: 'center',
  justifyContent: 'center',
  border: '1px solid var(--border-subtle)',
  borderRadius: 6,
  background: 'var(--bg-secondary)',
  color: 'var(--text-secondary)'
}

const contentStyle: CSSProperties = {
  padding: '16px 22px 20px',
  overflowY: 'auto'
}

const bodyStyle: CSSProperties = {
  margin: '0 0 14px',
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-body-size)',
  lineHeight: 'var(--type-body-line-height)'
}

const metaGridStyle: CSSProperties = {
  display: 'grid',
  gridTemplateColumns: 'repeat(2, minmax(0, 1fr))',
  gap: 10,
  marginBottom: 14
}

const metaItemStyle: CSSProperties = {
  minWidth: 0,
  padding: 10,
  border: '1px solid var(--border-subtle)',
  borderRadius: 8,
  background: 'var(--bg-secondary)'
}

const metaLabelStyle: CSSProperties = {
  display: 'block',
  marginBottom: 4,
  color: 'var(--text-dimmed)',
  fontSize: 11,
  lineHeight: '14px'
}

const metaValueStyle: CSSProperties = {
  display: 'block',
  color: 'var(--text-primary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  overflowWrap: 'anywhere'
}

const releaseNotesStyle: CSSProperties = {
  border: '1px solid var(--border-subtle)',
  borderRadius: 8,
  background: 'var(--bg-secondary)',
  overflow: 'hidden'
}

const releaseNotesTitleStyle: CSSProperties = {
  padding: '9px 11px',
  borderBottom: '1px solid var(--border-subtle)',
  color: 'var(--text-primary)',
  fontSize: 'var(--type-ui-size)',
  lineHeight: 'var(--type-ui-line-height)',
  fontWeight: 650
}

const releaseNotesBodyStyle: CSSProperties = {
  maxHeight: 156,
  overflowY: 'auto',
  padding: '10px 11px',
  whiteSpace: 'pre-wrap',
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  overflowWrap: 'anywhere'
}

const linkButtonStyle: CSSProperties = {
  display: 'inline-flex',
  alignItems: 'center',
  gap: 6,
  marginTop: 12,
  padding: '5px 8px',
  border: '1px solid var(--border-subtle)',
  borderRadius: 6,
  background: 'transparent',
  color: 'var(--text-secondary)',
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  cursor: 'pointer'
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

const progressBytesStyle: CSSProperties = {
  marginTop: 6,
  color: 'var(--text-dimmed)',
  fontSize: 11,
  lineHeight: '14px'
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
  justifyContent: 'flex-end',
  gap: 8,
  padding: '14px 22px',
  borderTop: '1px solid var(--border-subtle)'
}

function secondaryButtonStyle(disabled: boolean): CSSProperties {
  return {
    minWidth: 84,
    padding: '7px 12px',
    border: '1px solid var(--button-secondary-border, var(--border-default))',
    borderRadius: 6,
    background: 'var(--button-secondary-bg, var(--bg-tertiary))',
    color: 'var(--button-secondary-text, var(--text-primary))',
    fontSize: 'var(--type-ui-size)',
    lineHeight: 'var(--type-ui-line-height)',
    fontWeight: 'var(--type-ui-weight)',
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.55 : 1
  }
}

function primaryButtonStyle(disabled: boolean): CSSProperties {
  return {
    minWidth: 112,
    padding: '7px 12px',
    border: '1px solid transparent',
    borderRadius: 6,
    background: 'var(--accent)',
    color: 'var(--on-accent)',
    fontSize: 'var(--type-ui-size)',
    lineHeight: 'var(--type-ui-line-height)',
    fontWeight: 680,
    cursor: disabled ? 'default' : 'pointer',
    opacity: disabled ? 0.6 : 1
  }
}

const spinStyle: CSSProperties = {
  animation: 'spin 1s linear infinite'
}

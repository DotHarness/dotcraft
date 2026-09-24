import { useState } from 'react'
import { CircleAlert, RotateCw, Settings } from 'lucide-react'
import { Button } from './ui/Button'
import { CopyButton } from './ui/CopyButton'
import { Spinner } from './ui/Spinner'
import { useConnectionStore } from '../stores/connectionStore'
import { useT } from '../contexts/LocaleContext'
import { Avatar } from '@dotcraft/avatar/react'

interface ErrorScreenProps {
  onOpenSettings?: () => void
}

export function ErrorScreen({ onOpenSettings }: ErrorScreenProps = {}): JSX.Element | null {
  const t = useT()
  const { status, errorMessage, errorType, binarySource } = useConnectionStore()
  const [retryPending, setRetryPending] = useState(false)
  const [retryError, setRetryError] = useState<string | null>(null)

  if (status !== 'error') return null

  const isBinaryNotFound = errorType === 'binary-not-found'
  const isHandshakeTimeout = errorType === 'handshake-timeout'
  const isRemoteConfigInvalid = errorType === 'remote-config-invalid'

  const title = isBinaryNotFound
    ? t('error.title.binary')
    : isHandshakeTimeout
      ? t('error.title.timeout')
      : t('error.title.generic')

  const description = isBinaryNotFound
    ? binarySource === 'custom'
      ? t('error.desc.binary.custom')
      : binarySource === 'path'
        ? t('error.desc.binary.path')
        : t('error.desc.binary.bundled')
    : isHandshakeTimeout
      ? t('error.desc.timeout')
      : (errorMessage ?? t('error.desc.unexpected'))

  const actionLabel = isBinaryNotFound || isRemoteConfigInvalid
    ? t('error.action.openSettings')
    : isHandshakeTimeout
      ? t('error.action.restart')
      : t('error.action.retry')
  const displayedActionLabel = retryPending
    ? isHandshakeTimeout
      ? t('settings.action.restarting')
      : t('settings.action.connecting')
    : actionLabel
  const detailsText = [
    errorMessage,
    retryError ? `Retry failed: ${retryError}` : null
  ].filter((value): value is string => Boolean(value)).join('\n\n')

  // The action opens Settings (no spawn possible) for binary / invalid-remote-config;
  // otherwise it retries (or restarts) the connection. The leading icon follows suit.
  const isSettingsAction = isBinaryNotFound || isRemoteConfigInvalid
  const ActionIcon = isSettingsAction ? Settings : RotateCw

  async function handleAction(): Promise<void> {
    if (isBinaryNotFound || isRemoteConfigInvalid) {
      onOpenSettings?.()
      return
    }
    if (retryPending) return

    setRetryPending(true)
    setRetryError(null)
    try {
      await window.api.appServer.retryConnection({ restartManaged: isHandshakeTimeout })
    } catch (error) {
      setRetryError(error instanceof Error ? error.message : String(error))
    } finally {
      setRetryPending(false)
    }
  }

  return (
    <div
      style={{
        position: 'fixed',
        inset: 0,
        backgroundColor: 'var(--bg-primary)',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        zIndex: 9999,
        padding: '32px'
      }}
      role="alert"
      aria-live="assertive"
    >
      <div
        style={{
          textAlign: 'center',
          maxWidth: '480px'
        }}
      >
        {/* Decorative: the role="alert" container's title and description carry the
            semantics. `composer-mascot-motion` is also the reduced-motion scope. */}
        <div
          style={{ display: 'flex', justifyContent: 'center', marginBottom: '16px' }}
          aria-hidden="true"
        >
          <div
            className="composer-mascot-motion"
            style={{
              transformOrigin: 'bottom center',
              filter: 'drop-shadow(0 6px 9px color-mix(in srgb, #0b3d62 20%, transparent))'
            }}
          >
            <div style={{ transformOrigin: 'bottom center', transform: 'translateY(2px) rotate(-3deg) scale(0.98)' }}>
              <div className="composer-mascot-shake">
                <Avatar name="" state="blocked" size={96} />
              </div>
            </div>
          </div>
        </div>

        <h1
          style={{
            fontSize: '20px',
            fontWeight: 600,
            color: 'var(--text-primary)',
            marginBottom: '12px'
          }}
        >
          {title}
        </h1>

        <p
          style={{
            fontSize: '14px',
            color: 'var(--text-secondary)',
            lineHeight: 1.6,
            marginBottom: '28px'
          }}
        >
          {description}
        </p>

        <Button
          variant="primary"
          size="prominent"
          onClick={() => { void handleAction() }}
          disabled={retryPending}
          aria-busy={retryPending}
          iconLeft={retryPending ? <Spinner size={16} /> : <ActionIcon size={16} aria-hidden="true" />}
          style={{ minWidth: '220px' }}
        >
          {displayedActionLabel}
        </Button>

        {/* Always expanded rather than collapsible, so a bug report can be copied
            without an extra click. */}
        {detailsText && (
          <div
            style={{
              marginTop: '24px',
              textAlign: 'left',
              border: '1px solid var(--border-default)',
              borderRadius: '8px',
              backgroundColor: 'var(--bg-secondary)',
              overflow: 'hidden'
            }}
          >
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: '8px',
                padding: '9px 12px',
                borderBottom: '1px solid var(--border-default)'
              }}
            >
              <CircleAlert size={14} aria-hidden="true" style={{ color: 'var(--error)', flexShrink: 0 }} />
              <span style={{ flex: 1, fontSize: '12px', fontWeight: 600, color: 'var(--text-secondary)' }}>
                {t('error.details')}
              </span>
              <CopyButton getText={() => detailsText} label={t('error.details.copy')} copiedLabel={t('common.copied')} />
            </div>
            <pre
              style={{
                margin: 0,
                fontSize: '11px',
                lineHeight: 1.6,
                color: 'var(--error)',
                padding: '12px 14px',
                maxHeight: '180px',
                overflow: 'auto',
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-word'
              }}
            >
              {detailsText}
            </pre>
          </div>
        )}
      </div>
    </div>
  )
}

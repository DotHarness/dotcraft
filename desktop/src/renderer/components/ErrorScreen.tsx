import { useState } from 'react'
import { useConnectionStore } from '../stores/connectionStore'
import { useT } from '../contexts/LocaleContext'

interface ErrorScreenProps {
  onOpenSettings?: () => void
}

/**
 * Full-screen error display for fatal AppServer connection errors.
 * Spec §18.1.
 *
 * Shown when:
 * - AppServer binary not found (errorType: 'binary-not-found')
 * - Initialize handshake timeout (errorType: 'handshake-timeout')
 * - Persisted remote connection config is invalid (errorType: 'remote-config-invalid')
 */
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
        {/* Error icon */}
        <div
          style={{
            width: '56px',
            height: '56px',
            borderRadius: '50%',
            backgroundColor: 'rgba(239, 68, 68, 0.15)',
            border: '2px solid var(--error)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            margin: '0 auto 20px',
            fontSize: '24px',
            color: 'var(--error)'
          }}
          aria-hidden="true"
        >
          ✕
        </div>

        {/* Title */}
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

        {/* Description */}
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

        {/* Action button */}
        <button
          type="button"
          onClick={() => { void handleAction() }}
          disabled={retryPending}
          aria-busy={retryPending}
          style={{
            padding: '10px 24px',
            backgroundColor: 'var(--text-primary)',
            color: 'var(--bg-primary)',
            border: '1px solid var(--text-primary)',
            borderRadius: '8px',
            fontSize: '14px',
            fontWeight: 500,
            cursor: retryPending ? 'not-allowed' : 'pointer',
            opacity: retryPending ? 0.78 : 1,
            transition: 'background-color 150ms ease',
            boxShadow: 'var(--shadow-level-1)'
          }}
          onMouseEnter={(e) => {
            if (retryPending) return
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'color-mix(in srgb, var(--text-primary) 88%, var(--bg-primary))'
          }}
          onMouseLeave={(e) => {
            if (retryPending) return
            ;(e.currentTarget as HTMLButtonElement).style.backgroundColor = 'var(--text-primary)'
          }}
        >
          {displayedActionLabel}
        </button>

        {/* Error details (collapsible for debugging) */}
        {detailsText && (
          <details
            style={{
              marginTop: '20px',
              textAlign: 'left'
            }}
          >
            <summary
              style={{
                fontSize: '12px',
                color: 'var(--text-dimmed)',
                cursor: 'pointer',
                marginBottom: '8px'
              }}
            >
              {t('error.details')}
            </summary>
            <pre
              style={{
                fontSize: '11px',
                color: 'var(--error)',
                backgroundColor: 'var(--bg-secondary)',
                padding: '12px',
                borderRadius: '6px',
                overflow: 'auto',
                whiteSpace: 'pre-wrap',
                wordBreak: 'break-all',
                border: '1px solid var(--border-default)'
              }}
            >
              {detailsText}
            </pre>
          </details>
        )}
      </div>
    </div>
  )
}

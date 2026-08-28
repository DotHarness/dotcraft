import { useState } from 'react'
import { Check, CircleAlert, Copy, Loader2, RotateCw, Settings } from 'lucide-react'
import { useConnectionStore } from '../stores/connectionStore'
import { useT } from '../contexts/LocaleContext'
import { MascotRobot } from './conversation/MascotRobot'

interface ErrorScreenProps {
  onOpenSettings?: () => void
}

export function ErrorScreen({ onOpenSettings }: ErrorScreenProps = {}): JSX.Element | null {
  const t = useT()
  const { status, errorMessage, errorType, binarySource } = useConnectionStore()
  const [retryPending, setRetryPending] = useState(false)
  const [retryError, setRetryError] = useState<string | null>(null)
  const [copied, setCopied] = useState(false)

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

  async function handleCopy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(detailsText)
      setCopied(true)
      window.setTimeout(() => setCopied(false), 1400)
    } catch {
      // Clipboard may be unavailable (denied permission / insecure context); ignore.
    }
  }

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
              {/* Root carries -deflate so its descendant .mascot-glow blinks red ×3. */}
              <div className="composer-mascot-deflate">
                <div className="composer-mascot-shake">
                  <MascotRobot expression="operator" light="error" size={96} />
                </div>
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

        <button
          type="button"
          onClick={() => { void handleAction() }}
          disabled={retryPending}
          aria-busy={retryPending}
          style={{
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            gap: '8px',
            minWidth: '220px',
            padding: '11px 24px',
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
          {retryPending
            ? <Loader2 size={16} strokeWidth={2.2} className="animate-spin-custom" aria-hidden="true" />
            : <ActionIcon size={16} strokeWidth={1.8} aria-hidden="true" />}
          {displayedActionLabel}
        </button>

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
              <button
                type="button"
                onClick={() => { void handleCopy() }}
                aria-label={t('error.details.copy')}
                title={t('error.details.copy')}
                style={{
                  display: 'inline-flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  width: '26px',
                  height: '24px',
                  color: copied ? 'var(--success)' : 'var(--text-secondary)',
                  backgroundColor: 'var(--bg-primary)',
                  border: '1px solid var(--border-default)',
                  borderRadius: '6px',
                  cursor: 'pointer'
                }}
              >
                {copied
                  ? <Check size={13} aria-hidden="true" />
                  : <Copy size={13} aria-hidden="true" />}
              </button>
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

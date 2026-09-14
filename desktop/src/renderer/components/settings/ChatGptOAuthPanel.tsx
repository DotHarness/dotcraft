import { useEffect, useRef, useState, type JSX } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { addToast } from '../../stores/toastStore'
import { Button } from '../ui/Button'

interface ChatGptOAuthPanelProps {
  providerId: string
  providerInfo: { authMethod?: string; chatGptAccountId?: string | null; chatGptPlanType?: string | null } | null
  selectedProviderId: string | null
  selectedProviderUsable: boolean
  onAfterMutation: () => Promise<boolean>
  onSignedIn: (providerId: string) => void
  onProviderActivated?: (providerId: string) => void
}

export function ChatGptOAuthPanel({
  providerId,
  providerInfo,
  selectedProviderId,
  selectedProviderUsable,
  onAfterMutation,
  onSignedIn,
  onProviderActivated
}: ChatGptOAuthPanelProps): JSX.Element {
  const t = useT()
  const mounted = useRef(false)
  useEffect(() => {
    mounted.current = true
    return () => { mounted.current = false }
  }, [])
  const [refreshFailed, setRefreshFailed] = useState(false)
  const [pending, setPending] = useState(false)
  const [authorizeUrl, setAuthorizeUrl] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (!pending) return
    const unsubscribe = window.api.appServer.onNotification((payload) => {
      if (payload?.method === 'auth/openai/authorizeUrl') {
        const params = payload.params as { url?: string } | undefined
        if (typeof params?.url === 'string') {
          setAuthorizeUrl(params.url)
        }
      }
    })
    return () => unsubscribe()
  }, [pending])

  async function refreshProvider(): Promise<void> {
    setPending(true)
    const refreshed = await onAfterMutation()
    if (!mounted.current) return
    setRefreshFailed(!refreshed)
    setPending(false)
  }

  async function handleSignIn(): Promise<void> {
    setPending(true)
    setError(null)
    setAuthorizeUrl(null)
    try {
      const result = await window.api.appServer.sendRequest(
        'auth/openai/login',
        { providerId, openBrowser: true },
        15 * 60 * 1000 // up to 15 minutes for the user to complete browser flow
      )
      if (!mounted.current) return
      const savedId = (result as { providerId?: string })?.providerId || providerId
      onSignedIn(savedId)
      // Only auto-activate over an empty or broken selection, never over an intentional
      // one. Must run before onAfterMutation so reloadProviders() sees it in one pass.
      const shouldActivate =
        !selectedProviderId ||
        selectedProviderId === savedId ||
        !selectedProviderUsable
      let activated = false
      if (shouldActivate && selectedProviderId !== savedId) {
        try {
          await window.api.appServer.sendRequest(
            'workspace/config/update',
            { providerId: savedId },
            20_000
          )
          activated = true
          onProviderActivated?.(savedId)
        } catch (activateErr) {
          addToast(t('settings.llm.toast.saveProviderSelectionFailed', {
            error: activateErr instanceof Error ? activateErr.message : String(activateErr)
          }), 'error')
        }
      } else if (selectedProviderId === savedId) {
        // Already the active provider — no switch needed, but still treat as activated for messaging.
        activated = true
      }
      if (activated) {
        addToast(t('settings.llm.toast.chatgptActivated'), 'success')
      } else if (!shouldActivate) {
        addToast(t('settings.llm.toast.chatgptActivateSkipped'), 'info')
      }
      await refreshProvider()
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err)
      setError(message)
      addToast(t('settings.llm.authMethod.signInFailed', { error: message }), 'error')
    } finally {
      setPending(false)
      setAuthorizeUrl(null)
    }
  }

  async function handleSignOut(): Promise<void> {
    setPending(true)
    setError(null)
    try {
      await window.api.appServer.sendRequest('auth/openai/logout', { providerId }, 30_000)
      await refreshProvider()
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err)
      setError(message)
    } finally {
      setPending(false)
    }
  }

  async function handleCopyUrl(): Promise<void> {
    if (!authorizeUrl) return
    try {
      await navigator.clipboard.writeText(authorizeUrl)
      addToast(t('settings.llm.authMethod.urlCopied'), 'success')
    } catch {
      // Silent; user can still see the URL in the panel.
    }
  }

  const signedIn = providerInfo?.authMethod === 'chatgptOAuth' && Boolean(providerInfo?.chatGptAccountId)

  return (
    <div style={{ display: 'grid', gap: '12px' }}>
      {signedIn ? (
        <div
          style={{
            padding: '12px 14px',
            borderRadius: '8px',
            border: '1px solid var(--accent)',
            background: 'var(--bg-tertiary)',
            color: 'var(--text-primary)',
            fontSize: '13px',
            lineHeight: 1.55
          }}
        >
          <div style={{ fontWeight: 600 }}>
            {t('settings.llm.authMethod.signedInAs', {
              account: maskAccountId(providerInfo!.chatGptAccountId!),
              plan: providerInfo!.chatGptPlanType ?? 'unknown'
            })}
          </div>
        </div>
      ) : (
        <div
          style={{
            padding: '12px 14px',
            borderRadius: '8px',
            border: '1px dashed var(--border-default)',
            color: 'var(--text-secondary)',
            fontSize: '12px',
            lineHeight: 1.55
          }}
        >
          {t('settings.llm.authMethod.notSignedIn')}
        </div>
      )}

      {pending && authorizeUrl && (
        <div
          style={{
            padding: '10px 12px',
            borderRadius: '8px',
            border: '1px solid var(--border-default)',
            background: 'var(--bg-secondary)',
            display: 'flex',
            flexDirection: 'column',
            gap: '8px'
          }}
        >
          <div style={{ fontSize: '12px', color: 'var(--text-secondary)' }}>
            {t('settings.llm.authMethod.signInPending')}
          </div>
          <div
            style={{
              fontFamily: 'var(--font-mono, monospace)',
              fontSize: '11px',
              wordBreak: 'break-all',
              color: 'var(--text-primary)'
            }}
          >
            {authorizeUrl}
          </div>
          <Button
            onClick={() => void handleCopyUrl()}
            style={{ alignSelf: 'flex-start' }}
          >
            {t('settings.llm.authMethod.copyUrl')}
          </Button>
        </div>
      )}

      {refreshFailed && (
        <Button onClick={() => void refreshProvider()} disabled={pending}>
          {t('common.retry')}
        </Button>
      )}

      {error && (
        <div style={{ fontSize: '12px', color: 'var(--error)' }}>
          {error}
        </div>
      )}

      <div style={{ display: 'flex', gap: '8px', flexWrap: 'wrap' }}>
        <Button
          variant="primary"
          onClick={() => void handleSignIn()}
          disabled={pending || refreshFailed}
        >
          {pending
            ? t('settings.llm.authMethod.signInPending')
            : t('settings.llm.authMethod.signIn')}
        </Button>
        {signedIn && (
          <Button
            onClick={() => void handleSignOut()}
            disabled={pending}
          >
            {t('settings.llm.authMethod.signOut')}
          </Button>
        )}
      </div>
    </div>
  )
}

function maskAccountId(accountId: string): string {
  const trimmed = accountId.trim()
  if (trimmed.length <= 8) return trimmed
  return `${trimmed.slice(0, 4)}…${trimmed.slice(-4)}`
}

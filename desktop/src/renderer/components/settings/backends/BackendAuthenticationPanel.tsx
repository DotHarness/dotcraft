import { useEffect, useRef, useState, type JSX } from 'react'
import type { ProviderAuthLoginState, ProviderAuthStatusResult, ProviderAuthenticationMethodInfo } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { Button } from '../../ui/Button'
import { Input } from '../../ui/Input'
import { Select } from '../../ui/Select'
import styles from './ModelBackendSettings.module.css'

interface Props {
  providerId: string
  methods: readonly ProviderAuthenticationMethodInfo[]
  method: string
  onAuthenticated: () => void
}

export function BackendAuthenticationPanel({ providerId, methods, method, onAuthenticated }: Props): JSX.Element {
  const t = useT()
  const [status, setStatus] = useState<ProviderAuthStatusResult | null>(null)
  const [operation, setOperation] = useState<ProviderAuthLoginState | null>(null)
  const [answer, setAnswer] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const loginId = useRef<string | null>(null)
  const completedLogin = useRef<string | null>(null)
  const activity = useRef(0)
  const mounted = useRef(false)
  const onAuthenticatedRef = useRef(onAuthenticated)
  onAuthenticatedRef.current = onAuthenticated

  useEffect(() => {
    mounted.current = true
    let revision = -1
    let observedLogin: string | null = null
    const apply = (next: ProviderAuthLoginState): void => {
      if (!mounted.current || next.loginId !== loginId.current) return
      if (observedLogin !== next.loginId) {
        observedLogin = next.loginId
        revision = -1
      }
      if (next.revision <= revision) return
      revision = next.revision
      setOperation(next)
      if (next.status === 'completed' && completedLogin.current !== next.loginId) {
        completedLogin.current = next.loginId
        setStatus(next.authentication ?? null)
        setAnswer('')
        onAuthenticatedRef.current()
      }
    }
    const unsubscribe = window.api.appServer.onNotification((notification) => {
      if (notification.method === 'provider/auth/updated') apply(notification.params as ProviderAuthLoginState)
    })
    const timer = window.setInterval(() => {
      if (!loginId.current) return
      void window.api.appServer.sendRequest('provider/auth/state', { loginId: loginId.current })
        .then((state) => apply(state as ProviderAuthLoginState)).catch(() => {})
    }, 1000)
    const statusActivity = activity.current
    void window.api.appServer.sendRequest('provider/auth/status', { providerId }).then((value) => {
      if (mounted.current && activity.current === statusActivity) setStatus(value as ProviderAuthStatusResult)
    }).catch((cause) => {
      if (mounted.current) setError(String(cause))
    })
    return () => {
      mounted.current = false
      unsubscribe()
      window.clearInterval(timer)
      const id = loginId.current
      loginId.current = null
      if (id) void window.api.appServer.sendRequest('provider/auth/cancel', { loginId: id }).catch(() => {})
    }
  }, [providerId])

  async function login(): Promise<void> {
    activity.current++
    setBusy(true)
    setError(null)
    setAnswer('')
    setOperation(null)
    try {
      const next = await window.api.appServer.sendRequest('provider/auth/login', { providerId, method }) as ProviderAuthLoginState
      if (!mounted.current) {
        void window.api.appServer.sendRequest('provider/auth/cancel', { loginId: next.loginId }).catch(() => {})
        return
      }
      loginId.current = next.loginId
      setOperation(next)
      if (next.status === 'completed' && completedLogin.current !== next.loginId) {
        completedLogin.current = next.loginId
        setStatus(next.authentication ?? null)
        onAuthenticatedRef.current()
      }
    } catch (cause) { if (mounted.current) setError(String(cause)) }
    finally { if (mounted.current) setBusy(false) }
  }

  async function logout(): Promise<void> {
    activity.current++
    setBusy(true)
    setError(null)
    try {
      await window.api.appServer.sendRequest('provider/auth/logout', { providerId })
      if (!mounted.current) return
      loginId.current = null
      setOperation(null)
      setStatus({ isAuthenticated: false })
      onAuthenticatedRef.current()
    } catch (cause) { if (mounted.current) setError(String(cause)) }
    finally { if (mounted.current) setBusy(false) }
  }

  async function respond(): Promise<void> {
    if (!operation?.promptId) return
    setBusy(true)
    setError(null)
    try {
      await window.api.appServer.sendRequest('provider/auth/input', {
        loginId: operation.loginId, promptId: operation.promptId, value: answer
      })
      if (mounted.current) setAnswer('')
    } catch (cause) { if (mounted.current) setError(String(cause)) }
    finally { if (mounted.current) setBusy(false) }
  }

  const pending = operation?.status === 'running' || operation?.status === 'waiting'
  const prompt = operation?.prompt
  const event = operation?.event
  const supported = methods.some((item) => item.id === method && item.kind !== 'none' && item.kind !== 'ambient')
  return <div className={styles.stack}>
    <div role="status">{t(status?.isAuthenticated ? 'settings.llm.backends.authenticated' : 'settings.llm.backends.unauthenticated')}</div>
    {status?.displayName && <div>{status.displayName}</div>}
    {status?.failureMessage && <div role="alert">{status.failureMessage}</div>}
    <div className={styles.actions}>
      <Button disabled={busy || pending || !supported} onClick={() => void login()}>{t('settings.llm.backends.login')}</Button>
      <Button disabled={busy || pending} onClick={() => void logout()}>{t('settings.llm.backends.logout')}</Button>
      {pending && <Button onClick={() => {
        setAnswer('')
        void window.api.appServer.sendRequest('provider/auth/cancel', { loginId: operation!.loginId }).catch((cause) => setError(String(cause)))
      }}>{t('common.cancel')}</Button>}
    </div>
    {pending && event?.message && <p>{event.message}</p>}
    {pending && event?.url && <div className={styles.url}>
      <a href={event.url} target="_blank" rel="noreferrer">{event.url}</a>
    </div>}
    {pending && event?.userCode && <code>{event.userCode}</code>}
    {pending && prompt && <div className={styles.stack}>
      <label htmlFor={'backend-auth-' + providerId}>{prompt.message}</label>
      {prompt.kind === 'select' ? <Select
        id={'backend-auth-' + providerId} value={answer} ariaLabel={prompt.message}
        options={(prompt.choices ?? []).map((choice) => ({ value: choice.id, label: choice.label }))}
        onValueChange={setAnswer}
      /> : <Input id={'backend-auth-' + providerId} type={prompt.kind === 'secret' ? 'password' : 'text'}
        value={answer} autoComplete="off" onChange={(change) => setAnswer(change.target.value)} />}
      <Button disabled={busy || !answer} onClick={() => void respond()}>{t('settings.llm.backends.submit')}</Button>
    </div>}
    {(error || operation?.errorMessage) && <div role="alert" className={styles.error}>{error || operation?.errorMessage}</div>}
  </div>
}

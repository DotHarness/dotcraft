import { useEffect, useId, useState, type JSX } from 'react'
import type { ProviderAuthLoginState, ProviderAuthStatusResult } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { formatPlanName } from '../../../utils/chatgptPlan'
import { Button } from '../../ui/Button'
import { CopyButton } from '../../ui/CopyButton'
import { Input } from '../../ui/Input'
import { Select } from '../../ui/Select'
import { SettingsRow } from '../SettingsGroup'
import { useProviderSignIn, type ProviderSignInOptions } from './useProviderSignIn'
import styles from './ProviderSignIn.module.css'

interface ProviderSignInProps extends ProviderSignInOptions {
  title: string
  hint: string
  unavailable?: boolean
  disabled?: boolean
  onPendingChange?: (pending: boolean) => void
}

export function ProviderSignIn({
  title,
  hint,
  unavailable = false,
  disabled = false,
  onPendingChange,
  ...options
}: ProviderSignInProps): JSX.Element {
  const t = useT()
  const signIn = useProviderSignIn(options)
  const { status, login, pending, busy, error } = signIn
  const signedIn = status?.isAuthenticated === true && !pending

  useEffect(() => {
    onPendingChange?.(pending)
  }, [onPendingChange, pending])

  return (
    <>
      <SettingsRow
        label={title}
        description={
          <span role="status" aria-live="polite">
            {statusHint(status, login, pending, error, unavailable, hint, t)}
          </span>
        }
        control={pending ? (
          <Button onClick={() => void signIn.cancel()} disabled={login == null}>
            {t('common.cancel')}
          </Button>
        ) : signedIn ? (
          <Button onClick={() => void signIn.signOut()} loading={busy} disabled={disabled}>
            {t('settings.llm.signIn.signOut')}
          </Button>
        ) : (
          <Button onClick={() => void signIn.signIn()} loading={busy} disabled={disabled || unavailable}>
            {t('settings.llm.signIn.signIn')}
          </Button>
        )}
      />
      {login && <AuthorizationRow login={login} busy={busy} onAnswer={signIn.answer} />}
      {signedIn && error && (
        <SettingsRow>
          <span className={styles.message} role="alert">{error}</span>
        </SettingsRow>
      )}
    </>
  )
}

function AuthorizationRow({
  login,
  busy,
  onAnswer
}: {
  login: ProviderAuthLoginState
  busy: boolean
  onAnswer: (value: string) => Promise<boolean>
}): JSX.Element | null {
  const t = useT()
  const event = login.event
  const url = event?.kind === 'auth_url' || event?.kind === 'device_code' ? event.url : null
  const userCode = event?.kind === 'device_code' ? event.userCode : null
  const message = event && event.kind !== 'auth_url' && event.kind !== 'device_code' ? event.message : null
  const prompt = login.prompt && login.promptId ? login : null
  if (!url && !userCode && !message && !prompt) return null

  return (
    <SettingsRow
      orientation="block"
      label={t('settings.llm.signIn.authorization')}
      control={
        <div className={styles.authorization}>
          {url && (
            <a
              className={styles.link}
              href={url}
              onClick={(click) => {
                click.preventDefault()
                void window.api.shell.openExternal(url).catch(() => undefined)
              }}
            >
              {t('settings.llm.signIn.openAuthorizationPage')}
            </a>
          )}
          {userCode && (
            <div className={styles.code}>
              <span className={styles.message}>{t('settings.llm.signIn.code')}</span>
              <code className={styles.codeValue}>{userCode}</code>
              <CopyButton
                getText={() => userCode}
                label={t('settings.llm.signIn.copyCode')}
                copiedLabel={t('settings.llm.signIn.codeCopied')}
              />
            </div>
          )}
          {message && <span className={styles.message}>{message}</span>}
          {prompt && <PromptField key={prompt.promptId} login={prompt} busy={busy} onAnswer={onAnswer} />}
        </div>
      }
    />
  )
}

function PromptField({
  login,
  busy,
  onAnswer
}: {
  login: ProviderAuthLoginState
  busy: boolean
  onAnswer: (value: string) => Promise<boolean>
}): JSX.Element {
  const t = useT()
  const inputId = useId()
  const prompt = login.prompt!
  const choices = prompt.choices ?? []
  const [value, setValue] = useState(prompt.kind === 'select' ? choices[0]?.id ?? '' : '')

  useEffect(() => {
    if (prompt.kind === 'select' && !value && choices[0]) setValue(choices[0].id)
  }, [choices, prompt.kind, value])

  async function submit(): Promise<void> {
    if (!value || busy) return
    if (await onAnswer(value)) setValue('')
  }

  return (
    <form
      className={styles.prompt}
      onSubmit={(event) => {
        event.preventDefault()
        void submit()
      }}
    >
      <label className={styles.field} htmlFor={inputId}>
        <span className={styles.fieldLabel}>{prompt.message}</span>
        {prompt.kind === 'select' ? (
          <Select
            id={inputId}
            value={value}
            ariaLabel={prompt.message}
            options={choices.map((choice) => ({ value: choice.id, label: choice.label }))}
            onValueChange={setValue}
          />
        ) : (
          <Input
            id={inputId}
            type={prompt.kind === 'secret' ? 'password' : 'text'}
            value={value}
            autoComplete="off"
            spellCheck={false}
            onChange={(change) => setValue(change.target.value)}
          />
        )}
      </label>
      <div>
        <Button type="submit" variant="primary" disabled={!value} loading={busy}>
          {t('settings.llm.signIn.continue')}
        </Button>
      </div>
    </form>
  )
}

function statusHint(
  status: ProviderAuthStatusResult | null,
  login: ProviderAuthLoginState | null,
  pending: boolean,
  error: string | null,
  unavailable: boolean,
  hint: string,
  t: ReturnType<typeof useT>
): string {
  if (pending) {
    const browser = login?.event?.kind === 'auth_url' && !login.prompt
    return t(browser ? 'settings.llm.signIn.hintPending' : 'settings.llm.signIn.signingIn')
  }
  if (error && !status?.isAuthenticated) return t('settings.llm.signIn.failed', { error })
  if (unavailable) return t('settings.llm.signIn.hintUnavailable')
  if (status?.isAuthenticated) {
    const account = status.email?.trim() || status.displayName?.trim()
    const parts = [account, formatPlanName(status.planType)].filter(Boolean)
    return parts.length > 0 ? parts.join(' · ') : t('settings.llm.signIn.signedIn')
  }
  return status?.failureMessage?.trim() || hint
}

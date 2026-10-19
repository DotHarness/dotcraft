import { useState, type JSX } from 'react'
import type { ProviderBackendInfo } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { configKeyPath, readConfigValue, useConfigStore } from '../../../stores/configStore'
import { Button } from '../../ui/Button'
import { Input, Textarea } from '../../ui/Input'
import { Select } from '../../ui/Select'
import type { ProviderInfoWire } from '../providerInfo'
import { BackendAuthenticationPanel } from './BackendAuthenticationPanel'
import { parseBackendSettings } from './backendSettings'
import styles from './ModelBackendSettings.module.css'

interface Props {
  provider: ProviderInfoWire
  backend?: ProviderBackendInfo
  onChanged: () => Promise<unknown>
}

export function BackendProviderCard({ provider, backend, onChanged }: Props): JSX.Element {
  const t = useT()
  const config = useConfigStore((state) => state.config)
  const value = readConfigValue(config, configKeyPath('Providers', provider.id)) as Record<string, unknown> | undefined
  const read = (key: string): unknown => Object.entries(value ?? {}).find(([name]) => name.toLowerCase() === key.toLowerCase())?.[1]
  const [settings, setSettings] = useState(() => JSON.stringify(read('BackendSettings') ?? {}, null, 2))
  const [method, setMethod] = useState(() => String(read('AuthMethod') || backend?.authenticationMethods[0]?.id || ''))
  const [model, setModel] = useState(() =>
    String(readConfigValue(config, configKeyPath('ProviderPreferences', provider.id, 'Model')) ?? ''))
  const [models, setModels] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function refreshModels(): Promise<void> {
    setError(null)
    try {
      const result = await window.api.appServer.sendRequest('model/list', { providerId: provider.id }) as {
        success: boolean; models?: { id: string }[]; errorMessage?: string
      }
      if (!result.success) throw new Error(result.errorMessage || t('settings.llm.backends.modelError'))
      setModels((result.models ?? []).map((item) => item.id))
    } catch (cause) { setError(String(cause)) }
  }

  async function save(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      await useConfigStore.getState().write([
        { keyPath: configKeyPath('Providers', provider.id, 'BackendSettings'), value: parseBackendSettings(settings) },
        { keyPath: configKeyPath('Providers', provider.id, 'AuthMethod'), value: method }
      ])
      await onChanged()
    } catch (cause) { setError(String(cause)) }
    finally { setBusy(false) }
  }

  async function activate(): Promise<void> {
    setBusy(true)
    setError(null)
    try {
      await useConfigStore.getState().write([
        { keyPath: configKeyPath('ProviderPreferences', provider.id), value: { Model: model.trim() } },
        { keyPath: 'ProviderId', value: provider.id }
      ])
      await onChanged()
    } catch (cause) { setError(String(cause)) }
    finally { setBusy(false) }
  }

  return <details className={[styles.row, styles.details].join(' ')}>
    <summary>{provider.displayName} · {backend?.displayName || provider.backendId}</summary>
    <div className={styles.stack}>
      {!backend && <div role="alert">{t('settings.llm.backends.unavailable')}</div>}
      {backend && <>
        {backend.authenticationMethods.length > 0 && <BackendAuthenticationPanel key={provider.id + ':' + method} providerId={provider.id}
          methods={backend.authenticationMethods} method={method} onAuthenticated={() => {
            void refreshModels()
            void onChanged()
          }} />}
        <label className={styles.label}>{t('settings.llm.backends.model')}
          {models.length > 0 ? <Select value={model} ariaLabel={t('settings.llm.backends.model')}
            options={models.map((id) => ({ value: id, label: id }))} onValueChange={setModel} />
            : <Input value={model} onChange={(event) => setModel(event.target.value)} />}
        </label>
        <div className={styles.actions}>
          <Button onClick={() => void refreshModels()}>{t('settings.llm.backends.refreshModels')}</Button>
          <Button disabled={busy || !model.trim()} onClick={() => void activate()}>{t('settings.llm.backends.activate')}</Button>
        </div>
        <label className={styles.label}>{t('settings.llm.backends.authMethod')}
          <Select value={method} ariaLabel={t('settings.llm.backends.authMethod')}
            options={backend.authenticationMethods.map((item) => ({ value: item.id, label: item.displayName }))}
            onValueChange={async (value) => {
              try {
                await useConfigStore.getState().write([{ keyPath: configKeyPath('Providers', provider.id, 'AuthMethod'), value }])
                setMethod(value)
                await onChanged()
              } catch (cause) { setError(String(cause)) }
            }} />
        </label>
      </>}
      <label className={styles.label}>{t('settings.llm.backends.settings')}
        <Textarea mono rows={5} value={settings} onChange={(event) => setSettings(event.target.value)} />
      </label>
      <Button disabled={busy} onClick={() => void save()}>{t('settings.llm.backends.save')}</Button>
      {error && <div className={styles.error} role="alert">{error}</div>}
    </div>
  </details>
}

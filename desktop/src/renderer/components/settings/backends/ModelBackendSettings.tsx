import { useCallback, useEffect, useState, type JSX } from 'react'
import type { ProviderBackendInfo } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { useConfigStore } from '../../../stores/configStore'
import { Button } from '../../ui/Button'
import { Input, Textarea } from '../../ui/Input'
import { Select } from '../../ui/Select'
import { SettingsGroup } from '../SettingsGroup'
import { createBackendProvider } from '../providerConfigWrites'
import type { ProviderInfoWire } from '../providerInfo'
import { BackendProviderCard } from './BackendProviderCard'
import { parseBackendSettings } from './backendSettings'
import styles from './ModelBackendSettings.module.css'

interface Props {
  providers: readonly ProviderInfoWire[]
  onChanged: () => Promise<unknown>
}

export function ModelBackendSettings({ providers, onChanged }: Props): JSX.Element | null {
  const t = useT()
  const [backends, setBackends] = useState<ProviderBackendInfo[]>([])
  const [backendId, setBackendId] = useState('')
  const [providerId, setProviderId] = useState('')
  const [settings, setSettings] = useState('{}')
  const [method, setMethod] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)
  const load = useCallback(async () => {
    const result = await window.api.appServer.sendRequest('provider/backend/list', {}) as { backends: ProviderBackendInfo[] }
    setBackends(Array.isArray(result?.backends) ? result.backends : [])
  }, [])
  useEffect(() => { void load().catch((cause) => setError(String(cause))) }, [load])
  const instances = providers.filter((provider) => provider.backendId)
  const selected = backends.find((backend) => backend.id === backendId)
  if (backends.length === 0 && instances.length === 0) return null

  async function create(): Promise<void> {
    if (!selected || !providerId.trim()) return
    setBusy(true)
    setError(null)
    try {
      await createBackendProvider(providerId.trim(), {
        BackendId: selected.id, Protocol: selected.defaultProtocol,
        AuthMethod: method, BackendSettings: parseBackendSettings(settings)
      })
      await useConfigStore.getState().refresh()
      await onChanged()
      setProviderId('')
      setSettings('{}')
    } catch (cause) { setError(String(cause)) }
    finally { setBusy(false) }
  }

  return <SettingsGroup title={t('settings.llm.backends.title')} flush
    headerAction={<Button variant="ghost" onClick={() => void load().catch((cause) => setError(String(cause)))}>
      {t('settings.llm.backends.refresh')}
    </Button>}>
    {instances.map((provider) => <BackendProviderCard key={provider.id} provider={provider}
      backend={backends.find((backend) => backend.id === provider.backendId)} onChanged={onChanged} />)}
    {backends.length > 0 && <details className={[styles.row, styles.details].join(' ')}>
      <summary>{t('settings.llm.backends.add')}</summary>
      <div className={styles.stack}>
        <label className={styles.label}>{t('settings.llm.backends.backend')}
          <Select value={backendId} ariaLabel={t('settings.llm.backends.backend')}
            options={backends.map((backend) => ({ value: backend.id, label: backend.displayName }))}
            onValueChange={(id) => {
              setBackendId(id)
              setMethod(backends.find((backend) => backend.id === id)?.authenticationMethods[0]?.id ?? '')
            }} />
        </label>
        <label className={styles.label}>{t('settings.llm.backends.providerId')}
          <Input value={providerId} onChange={(event) => setProviderId(event.target.value)} />
        </label>
        <label className={styles.label}>{t('settings.llm.backends.authMethod')}
          <Select value={method} ariaLabel={t('settings.llm.backends.authMethod')}
            options={(selected?.authenticationMethods ?? []).map((item) => ({ value: item.id, label: item.displayName }))}
            onValueChange={setMethod} />
        </label>
        <label className={styles.label}>{t('settings.llm.backends.settings')}
          <Textarea rows={5} mono value={settings} onChange={(event) => setSettings(event.target.value)} />
        </label>
        <Button variant="primary" loading={busy} disabled={!selected || !providerId.trim()} onClick={() => void create()}>
          {t('settings.llm.backends.create')}
        </Button>
      </div>
    </details>}
    {error && <div className={[styles.row, styles.error].join(' ')} role="alert">{error}</div>}
  </SettingsGroup>
}

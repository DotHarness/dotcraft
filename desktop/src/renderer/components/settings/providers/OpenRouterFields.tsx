import { useEffect, useState, type Dispatch, type JSX, type SetStateAction } from 'react'
import type { JsonValue, ProviderRoutingListResult } from '@dotcraft/sdk/contracts'
import { useT } from '../../../contexts/LocaleContext'
import { Select } from '../../ui/Select'
import { TokenPicker, type TokenPickerOption } from '../../ui/TokenPicker'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import type { ProviderDraft } from './providerDraft'
import type { ProviderInfoWire } from '../providerInfo'
import styles from './OpenRouterFields.module.css'

export function OpenRouterFields({ draft, setDraft, provider }: {
  draft: ProviderDraft
  setDraft: Dispatch<SetStateAction<ProviderDraft>>
  provider?: ProviderInfoWire | null
}): JSX.Element {
  const t = useT()
  const settings = draft.backendSettings ?? {}
  const [suppliers, setSuppliers] = useState<TokenPickerOption[]>([])
  const [status, setStatus] = useState<'loading' | 'ready' | 'error'>('loading')
  const providerId = provider?.id
  const endPoint = provider?.endPoint
  useEffect(() => {
    let active = true
    setStatus('loading')
    void window.api.appServer.sendRequest('provider/routing/list', {
      ...(providerId ? { providerId, endPoint } : { backendId: 'openrouter' })
    }, 20_000).then((result) => {
      if (!active) return
      setSuppliers((result as ProviderRoutingListResult).providers.map((item) => ({ value: item.id, label: item.displayName })))
      setStatus('ready')
    }).catch(() => { if (active) setStatus('error') })
    return () => { active = false }
  }, [providerId, endPoint])
  function update(name: string, value: JsonValue | undefined): void {
    setDraft((current) => {
      const next = { ...current.backendSettings }
      if (value === undefined) delete next[name]
      else next[name] = value
      return { ...current, backendSettings: next }
    })
  }
  return <SettingsGroup title="OpenRouter">
    <SettingsRow label={t('settings.llm.openrouter.protocol')} control={<Select
      ariaLabel={t('settings.llm.openrouter.protocol')}
      value={draft.protocol || 'auto'}
      options={[
        { value: 'auto', label: t('settings.llm.openrouter.auto') },
        { value: 'anthropic', label: 'Anthropic Messages' },
        { value: 'openai-responses', label: 'OpenAI Responses' },
        { value: 'openai-chat-completions', label: 'OpenAI Chat Completions' }
      ]}
      onValueChange={(protocol) => setDraft((current) => ({ ...current, protocol }))}
    />} />
    {(['order', 'only', 'ignore'] as const).map((name) => <SettingsRow key={name}
      label={t(`settings.llm.openrouter.${name}`)} description={name === 'order' ? t('settings.llm.openrouter.providerOrderHint') : undefined}
      control={<TokenPicker values={Array.isArray(settings[name]) ? settings[name] as string[] : []} options={suppliers}
        ordered={name === 'order'} ariaLabel={t(`settings.llm.openrouter.${name}`)}
        addLabel={t('settings.llm.openrouter.addProvider')} placeholder={t('settings.llm.openrouter.providerPlaceholder')}
        onValuesChange={(values) => update(name, values.length ? values : undefined)} />} />)}
    {status !== 'ready' && <div className={styles.notice} role="status">
      {t(status === 'loading' ? 'settings.llm.openrouter.providersLoading' : 'settings.llm.openrouter.providersUnavailable')}
    </div>}
    <SettingsRow label={t('settings.llm.openrouter.sort')} control={<Select
      ariaLabel={t('settings.llm.openrouter.sort')}
      value={typeof settings.sort === 'string' ? settings.sort : ''}
      options={(['', 'price', 'latency', 'throughput'] as const).map((value) => ({ value,
        label: t(`settings.llm.openrouter.${value || 'default'}`) }))}
      onValueChange={(value) => update('sort', value || undefined)}
    />} />
    {(['allow_fallbacks', 'require_parameters'] as const).map((name) => <SettingsRow key={name}
      label={t(`settings.llm.openrouter.${name}`)} control={<Select
        ariaLabel={t(`settings.llm.openrouter.${name}`)}
        value={typeof settings[name] === 'boolean' ? String(settings[name]) : ''}
        options={[
          { value: '', label: t('settings.llm.openrouter.default') },
          { value: 'true', label: t('settings.llm.openrouter.enabled') },
          { value: 'false', label: t('settings.llm.openrouter.disabled') }
        ]}
        onValueChange={(value) => update(name, value === '' ? undefined : value === 'true')}
      />} />)}
  </SettingsGroup>
}

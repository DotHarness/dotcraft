import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useConfigSetting } from '../../../../stores/configStore'
import { Button } from '../../../ui/Button'
import type { ProviderInfoWire } from '../../providerInfo'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SegmentedControl } from '../../ui/SegmentedControl'
import {
  SettingsSelect,
  type SettingsSelectOption
} from '../../ui/SettingsSelect'
import { WebSearchApiKeyRow } from './WebSearchApiKeyRow'
import {
  canProviderSearchDeepSeek,
  isSearchProviderAuthenticated,
  normalizeSearchProvider
} from './webSearchModel'
import styles from './WebSearchSettingsGroup.module.css'

type CredentialSource = 'Provider' | 'ApiKey'

interface Props {
  providers: ProviderInfoWire[]
  providersLoading: boolean
}

export function WebSearchSettingsGroup({
  providers,
  providersLoading
}: Props): JSX.Element {
  const t = useT()
  const failureMessage = (error: string): string =>
    t('settings.llm.webSearch.saveFailed', { error })
  const service = useConfigSetting('Tools.Web.SearchProvider', failureMessage)
  const source = useConfigSetting(
    'Tools.Web.DeepSeekCredentialSource',
    failureMessage
  )
  const provider = useConfigSetting(
    'Tools.Web.DeepSeekProvider',
    failureMessage
  )
  const selected = normalizeSearchProvider(service.value)
  const credentialSource: CredentialSource =
    typeof source.value === 'string' && source.value.toLowerCase() === 'apikey'
      ? 'ApiKey'
      : 'Provider'
  const storedProviderId = typeof provider.value === 'string' ? provider.value : ''
  const candidates = providers.filter(canProviderSearchDeepSeek)
  const providerId =
    candidates.find((item) => item.id.toLowerCase() === storedProviderId.toLowerCase())?.id ??
    storedProviderId
  const staleProvider = providerId !== '' && !candidates.some((item) => item.id === providerId)
  const options: SettingsSelectOption[] = [
    {
      value: 'Auto',
      label: t('settings.llm.webSearch.auto'),
      description: t('settings.llm.webSearch.autoDescription')
    },
    {
      value: 'Exa',
      label: 'Exa',
      description: t('settings.llm.webSearch.exaDescription')
    },
    {
      value: 'Bing',
      label: 'Bing',
      description: t('settings.llm.webSearch.bingDescription')
    },
    {
      value: 'DeepSeek',
      label: 'DeepSeek',
      description: t('settings.llm.webSearch.deepSeekDescription')
    }
  ]
  const providerOptions: SettingsSelectOption[] = [
    {
      value: '',
      label: t('settings.llm.webSearch.reuseAuto'),
      description: t('settings.llm.webSearch.reuseAutoHint')
    },
    ...candidates.map((item) => ({
      value: item.id,
      label: item.displayName,
      disabled: !isSearchProviderAuthenticated(item),
      description: isSearchProviderAuthenticated(item)
        ? undefined
        : t('settings.llm.webSearch.unauthenticated')
    }))
  ]
  if (staleProvider) {
    providerOptions.push({
      value: providerId,
      label: providerId,
      disabled: true,
      description: t('settings.llm.webSearch.unavailable')
    })
  }
  const onlyProvider = candidates.length === 1 && !staleProvider ? candidates[0] : null
  const credentialsHint = credentialSource === 'ApiKey'
    ? t('settings.llm.webSearch.credentialsKeyHint')
    : onlyProvider
      ? isSearchProviderAuthenticated(onlyProvider)
        ? t('settings.llm.webSearch.credentialsProviderHint', { name: onlyProvider.displayName })
        : t('settings.llm.webSearch.credentialsUnauthenticated', { name: onlyProvider.displayName })
      : candidates.length === 0 && !staleProvider
        ? t('settings.llm.webSearch.deepSeekDescription')
        : t('settings.llm.webSearch.credentialsChosenHint')
  const showProviderChoice = candidates.length > 1 || staleProvider
  const noProviders = !providersLoading && candidates.length === 0 && !staleProvider
  return (
    <SettingsGroup title={t('settings.llm.webSearch.title')}>
      <SettingsRow
        label={t('settings.llm.webSearch.service')}
        description={t('settings.llm.webSearch.serviceHint')}
        htmlFor="settings-web-search-provider"
        control={
          <div className={styles.control}>
            <SettingsSelect
              id="settings-web-search-provider"
              value={selected}
              options={options}
              adaptiveWidth={false}
              disabled={service.pending}
              ariaLabel={t('settings.llm.webSearch.service')}
              onValueChange={(value) => void service.set(value)}
            />
          </div>
        }
      />
      {(selected === 'Exa' || selected === 'Auto') && (
        <WebSearchApiKeyRow key="Exa" provider="Exa" optional={selected === 'Auto'} />
      )}
      {selected === 'DeepSeek' && (
        <>
          <SettingsRow
            label={t('settings.llm.webSearch.credentials')}
            description={credentialsHint}
            control={
              <SegmentedControl<CredentialSource>
                value={credentialSource}
                disabled={source.pending}
                ariaLabel={t('settings.llm.webSearch.credentials')}
                options={[
                  { value: 'Provider', label: t('settings.llm.webSearch.reuseProvider') },
                  { value: 'ApiKey', label: t('settings.llm.webSearch.independentKey') }
                ]}
                onChange={(value) => void source.set(value)}
              />
            }
          />
          {credentialSource === 'ApiKey' ? (
            <WebSearchApiKeyRow key="DeepSeek" provider="DeepSeek" />
          ) : noProviders ? (
            <SettingsRow
              label={t('settings.llm.webSearch.deepSeekProvider')}
              description={t('settings.llm.webSearch.noProviders')}
              control={
                <Button size="sm" disabled={source.pending} onClick={() => void source.set('ApiKey')}>
                  {t('settings.llm.webSearch.useApiKey')}
                </Button>
              }
            />
          ) : showProviderChoice && (
            <SettingsRow
              label={t('settings.llm.webSearch.deepSeekProvider')}
              description={t('settings.llm.webSearch.deepSeekProviderHint')}
              htmlFor="settings-web-search-deepseek-provider"
              control={
                <div className={styles.control}>
                  <SettingsSelect
                    id="settings-web-search-deepseek-provider"
                    value={providerId}
                    options={providerOptions}
                    adaptiveWidth={false}
                    disabled={provider.pending || providersLoading}
                    ariaLabel={t('settings.llm.webSearch.deepSeekProvider')}
                    onValueChange={(value) => void provider.set(value)}
                  />
                </div>
              }
            />
          )}
        </>
      )}
    </SettingsGroup>
  )
}

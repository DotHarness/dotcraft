import type { Dispatch, JSX, SetStateAction } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { ANTHROPIC_PROTOCOL, defaultProviderEndpoint } from '../../../../shared/providerProtocols'
import { SecretInput } from '../../channels/FormShared'
import { Input } from '../../ui/Input'
import { SettingsGroup, SettingsRow } from '../SettingsGroup'
import { settingsHintStyle, settingsSectionLabelStyle as sectionLabelStyle } from '../settingsTypography'
import type { ProviderInfoWire } from '../providerInfo'
import {
  draftBuiltInProtocol,
  isStoredApiKey,
  withDefaultImageGenerationSupport,
  type ProviderDraft
} from './providerDraft'
import { DEEPSEEK_ENDPOINT, OPENROUTER_BACKEND_ID, OPENROUTER_ENDPOINT } from './providerPresets'
import styles from './ProviderEditor.module.css'

interface ProviderFieldProps {
  draft: ProviderDraft
  setDraft: Dispatch<SetStateAction<ProviderDraft>>
}

export function ProviderKeyFields({
  draft,
  setDraft,
  savedProvider,
  showEndpoint
}: ProviderFieldProps & { savedProvider: ProviderInfoWire | null; showEndpoint: boolean }): JSX.Element {
  const t = useT()
  return (
    <SettingsGroup title={t('settings.llm.connectionTitle')} flush>
      <div className={styles.stack}>
        <div>
          <label htmlFor="settings-provider-api-key" style={sectionLabelStyle()}>
            {t('settings.llm.field.apiKey')}
          </label>
          <SecretInput
            value={draft.apiKey}
            ariaLabel={t('settings.llm.field.apiKey')}
            onChange={(apiKey) => setDraft((current) => ({ ...current, apiKey }))}
            mono
            placeholder={t('settings.llm.field.apiKeyPlaceholder')}
          />
          {savedProvider?.hasApiKey === true && isStoredApiKey(draft) && (
            <div className={styles.hint} style={settingsHintStyle(false)}>
              {t('settings.llm.field.apiKeyKeepHint')}
            </div>
          )}
        </div>
        {showEndpoint && <ProviderEndpointField draft={draft} setDraft={setDraft} />}
      </div>
    </SettingsGroup>
  )
}

function ProviderEndpointField({ draft, setDraft }: ProviderFieldProps): JSX.Element {
  const t = useT()
  return (
    <div>
      <label htmlFor="settings-provider-endpoint" style={sectionLabelStyle()}>
        {t('settings.llm.field.endpoint')}
      </label>
      <ProviderEndpointInput draft={draft} setDraft={setDraft} />
    </div>
  )
}

export function ProviderEndpointRow({ draft, setDraft }: ProviderFieldProps): JSX.Element {
  const t = useT()
  const description = draft.backendId === OPENROUTER_BACKEND_ID
    ? t('settings.llm.openrouter.endpointHint')
    : draft.preset === 'deepseek'
      ? undefined
      : t(draftBuiltInProtocol(draft) === ANTHROPIC_PROTOCOL ? 'settings.llm.field.endpointAnthropicHint' : 'settings.llm.field.endpointOpenAiHint')
  return (
    <SettingsRow
      label={t('settings.llm.field.endpoint')}
      description={description}
      htmlFor="settings-provider-endpoint"
      orientation="block"
      control={<ProviderEndpointInput draft={draft} setDraft={setDraft} />}
    />
  )
}

function ProviderEndpointInput({ draft, setDraft }: ProviderFieldProps): JSX.Element {
  return (
    <Input
      id="settings-provider-endpoint"
      type="url"
      value={draft.endPoint}
      onChange={(e) => setDraft((current) => withDefaultImageGenerationSupport({
        ...current,
        endPoint: e.target.value
      }))}
      mono
      placeholder={draft.backendId === OPENROUTER_BACKEND_ID
        ? OPENROUTER_ENDPOINT
        : draft.preset === 'deepseek'
          ? DEEPSEEK_ENDPOINT
          : draft.preset === 'custom' ? 'https://' : defaultProviderEndpoint(draftBuiltInProtocol(draft))}
    />
  )
}

import type { Dispatch, JSX, SetStateAction } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { ANTHROPIC_PROTOCOL, defaultProviderEndpoint } from '../../../../shared/providerProtocols'
import { SecretInput } from '../../channels/FormShared'
import { Input } from '../../ui/Input'
import { SettingsGroup } from '../SettingsGroup'
import { settingsHintStyle, settingsSectionLabelStyle as sectionLabelStyle } from '../settingsTypography'
import type { ProviderInfoWire } from '../providerInfo'
import {
  draftBuiltInProtocol,
  isStoredApiKey,
  withDefaultImageGenerationSupport,
  type ProviderDraft
} from './providerDraft'
import styles from './ProviderEditor.module.css'

interface ProviderKeyFieldsProps {
  draft: ProviderDraft
  setDraft: Dispatch<SetStateAction<ProviderDraft>>
  savedProvider: ProviderInfoWire | null
}

export function ProviderKeyFields({ draft, setDraft, savedProvider }: ProviderKeyFieldsProps): JSX.Element {
  const t = useT()
  const protocol = draftBuiltInProtocol(draft)
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

        <div>
          <label htmlFor="settings-provider-endpoint" style={sectionLabelStyle()}>
            {t('settings.llm.field.endpoint')}
          </label>
          <Input
            id="settings-provider-endpoint"
            type="url"
            value={draft.endPoint}
            onChange={(e) => setDraft((current) => withDefaultImageGenerationSupport({
              ...current,
              endPoint: e.target.value
            }))}
            mono
            placeholder={defaultProviderEndpoint(protocol)}
          />
          <div className={styles.hint} style={settingsHintStyle(false)}>
            {protocol === ANTHROPIC_PROTOCOL
              ? t('settings.llm.field.endpointAnthropicHint')
              : t('settings.llm.field.endpointOpenAiHint')}
          </div>
        </div>
      </div>
    </SettingsGroup>
  )
}

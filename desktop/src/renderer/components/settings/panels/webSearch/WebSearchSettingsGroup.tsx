import { useState, type FocusEvent, type JSX, type KeyboardEvent } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useConfigSetting } from '../../../../stores/configStore'
import { SecretInput } from '../../../channels/FormShared'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SettingsSelect, type SettingsSelectOption } from '../../ui/SettingsSelect'
import { EXA_KEY_MASK, isSecretStored, resolveExaKeyCommit } from './webSearchModel'

const SELECT_ID = 'settings-web-search-provider'
const KEY_INPUT_ID = 'settings-web-search-exa-key'

export function WebSearchSettingsGroup(): JSX.Element {
  const t = useT()
  const failureMessage = (error: string): string => t('settings.llm.webSearch.saveFailed', { error })
  const providerSetting = useConfigSetting('Tools.Web.SearchProvider', failureMessage)
  const searchProvider = typeof providerSetting.value === 'string' && providerSetting.value.toLowerCase() === 'bing'
    ? 'Bing'
    : 'Exa'
  const keySetting = useConfigSetting('Tools.Web.ExaApiKey', failureMessage)
  const keyStored = isSecretStored(keySetting.value)
  const [keyDraft, setKeyDraft] = useState<string | null>(null)

  const commitKey = (): void => {
    const commit = resolveExaKeyCommit(keyDraft, keyStored)
    setKeyDraft(null)
    if (commit.kind === 'set') void keySetting.set(commit.value)
    else if (commit.kind === 'clear') void keySetting.set(null)
  }

  const handleKeyBlur = (event: FocusEvent<HTMLDivElement>): void => {
    if (!event.currentTarget.contains(event.relatedTarget as Node | null)) commitKey()
  }

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>): void => {
    if (event.key === 'Enter') commitKey()
    else if (event.key === 'Escape') setKeyDraft(null)
  }

  const options: SettingsSelectOption[] = [
    { value: 'Exa', label: 'Exa', description: t('settings.llm.webSearch.exaDescription') },
    { value: 'Bing', label: 'Bing', description: t('settings.llm.webSearch.bingDescription') }
  ]

  return (
    <SettingsGroup title={t('settings.llm.webSearch.title')}>
      <SettingsRow
        label={t('settings.llm.webSearch.service')}
        description={t('settings.llm.webSearch.serviceHint')}
        htmlFor={SELECT_ID}
        control={
          <SettingsSelect
            id={SELECT_ID}
            value={searchProvider}
            options={options}
            disabled={providerSetting.pending}
            ariaLabel={t('settings.llm.webSearch.service')}
            style={{ width: '240px' }}
            onValueChange={(value) => void providerSetting.set(value)}
          />
        }
      />
      {searchProvider === 'Exa' && (
        <SettingsRow
          label={t('settings.llm.webSearch.exaKey')}
          description={t('settings.llm.webSearch.exaKeyHint')}
          htmlFor={KEY_INPUT_ID}
          control={
            <div style={{ width: '240px' }} onBlur={handleKeyBlur} onKeyDown={handleKeyDown}>
              <SecretInput
                id={KEY_INPUT_ID}
                value={keyDraft ?? (keyStored ? EXA_KEY_MASK : '')}
                placeholder={t('settings.llm.webSearch.exaKeyPlaceholder')}
                ariaLabel={t('settings.llm.webSearch.exaKey')}
                disabled={keySetting.pending}
                onChange={setKeyDraft}
                mono
              />
            </div>
          }
        />
      )}
    </SettingsGroup>
  )
}

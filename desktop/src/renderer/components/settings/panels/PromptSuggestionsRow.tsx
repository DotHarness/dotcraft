import type { JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { useConfigSetting } from '../../../stores/configStore'
import { PillSwitch } from '../../ui/PillSwitch'
import { SettingsRow } from '../SettingsGroup'

export function PromptSuggestionsRow(): JSX.Element {
  const t = useT()
  const setting = useConfigSetting('PromptSuggestions.Enabled', (error) =>
    t('settings.personalization.promptSuggestionsSaveFailed', { error }))
  return (
    <SettingsRow
      label={t('settings.personalization.promptSuggestions')}
      description={t('settings.personalization.promptSuggestionsHint')}
      control={
        <PillSwitch
          checked={setting.value === true}
          disabled={setting.pending}
          aria-label={t('settings.personalization.promptSuggestions')}
          onChange={(checked) => { void setting.set(checked) }}
        />
      }
    />
  )
}

import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useComposerPreferencesStore } from '../../../../stores/composerPreferencesStore'
import { useConfigSetting } from '../../../../stores/configStore'
import { useConnectionStore } from '../../../../stores/connectionStore'
import { PillSwitch } from '../../../ui/PillSwitch'
import { SettingsRow } from '../../SettingsGroup'

export function InstantInterruptRow(): JSX.Element | null {
  const t = useT()
  const setting = useConfigSetting('InstantInterruptEnabled', (error) =>
    t('settings.followUpBehavior.instantInterrupt.saveFailed', { error }))
  const steer = useComposerPreferencesStore((state) => state.followUpQueueMode === 'steer')
  const available = useConnectionStore((state) => state.capabilities?.workspaceConfigManagement === true)
  if (!steer || !available) return null

  return (
    <SettingsRow
      label={t('settings.followUpBehavior.instantInterrupt.label')}
      description={t('settings.followUpBehavior.instantInterrupt.description')}
      control={
        <PillSwitch
          checked={setting.value === true}
          disabled={setting.pending}
          aria-label={t('settings.followUpBehavior.instantInterrupt.label')}
          onChange={(checked) => { void setting.set(checked) }}
        />
      }
    />
  )
}

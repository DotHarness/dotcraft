import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useComposerPreferencesStore } from '../../../../stores/composerPreferencesStore'
import { useConnectionStore } from '../../../../stores/connectionStore'
import { PillSwitch } from '../../../ui/PillSwitch'
import { SettingsRow } from '../../SettingsGroup'
import type { InstantInterruptSettings } from './useInstantInterruptSettings'

export function InstantInterruptRow({ settings }: { settings: InstantInterruptSettings }): JSX.Element | null {
  const t = useT()
  const steer = useComposerPreferencesStore((state) => state.followUpQueueMode === 'steer')
  const available = useConnectionStore((state) => state.capabilities?.workspaceConfigManagement === true)
  if (!steer || !available) return null

  return (
    <SettingsRow
      label={t('settings.followUpBehavior.instantInterrupt.label')}
      description={t('settings.followUpBehavior.instantInterrupt.description')}
      control={
        <PillSwitch
          checked={settings.enabled}
          disabled={settings.pending}
          aria-label={t('settings.followUpBehavior.instantInterrupt.label')}
          onChange={(checked) => { void settings.setEnabled(checked) }}
        />
      }
    />
  )
}

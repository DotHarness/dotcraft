import type { JSX } from 'react'
import type { FollowUpQueueMode } from '../../../../shared/desktopSettings'
import { useT } from '../../../contexts/LocaleContext'
import { useComposerPreferencesStore } from '../../../stores/composerPreferencesStore'
import { addToast } from '../../../stores/toastStore'
import { SettingsRow } from '../SettingsGroup'
import { SegmentedControl } from '../ui/SegmentedControl'

export function FollowUpBehaviorRow(): JSX.Element {
  const t = useT()
  const mode = useComposerPreferencesStore((state) => state.followUpQueueMode)
  const saving = useComposerPreferencesStore((state) => state.saving)
  const save = useComposerPreferencesStore((state) => state.saveFollowUpQueueMode)

  async function changeMode(next: FollowUpQueueMode): Promise<void> {
    try {
      await save(next)
    } catch (error) {
      addToast(t('settings.followUpBehavior.saveFailed', {
        error: error instanceof Error ? error.message : String(error)
      }), 'error')
    }
  }

  return (
    <SettingsRow
      label={t('settings.followUpBehavior.label')}
      description={t('settings.followUpBehavior.description')}
      control={
        <SegmentedControl
          value={mode}
          options={[
            { value: 'queue', label: t('settings.followUpBehavior.queue') },
            { value: 'steer', label: t('settings.followUpBehavior.steer') }
          ]}
          ariaLabel={t('settings.followUpBehavior.label')}
          disabled={saving}
          onChange={(next) => { void changeMode(next) }}
        />
      }
    />
  )
}

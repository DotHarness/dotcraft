import { useEffect, type JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { useFeatureGuideStore } from '../../../stores/featureGuideStore'
import { addToast } from '../../../stores/toastStore'
import { PillSwitch } from '../../ui/PillSwitch'
import { SettingsRow } from '../SettingsGroup'

export function FeatureTipsRow(): JSX.Element {
  const t = useT()
  const enabled = useFeatureGuideStore((state) => state.settings?.enabled ?? true)
  const loaded = useFeatureGuideStore((state) => state.settings !== null)

  useEffect(() => {
    void useFeatureGuideStore.getState().load().catch(() => {})
  }, [])

  return (
    <SettingsRow
      label={t('settings.general.featureTips')}
      description={t('settings.general.featureTipsHint')}
      control={
        <PillSwitch
          checked={enabled}
          disabled={!loaded}
          aria-label={t('settings.general.featureTips')}
          onChange={(checked) => {
            void useFeatureGuideStore.getState().setEnabled(checked).catch((error: unknown) => {
              addToast(t('settings.saveFailed', { error: error instanceof Error ? error.message : String(error) }), 'error')
            })
          }}
        />
      }
    />
  )
}

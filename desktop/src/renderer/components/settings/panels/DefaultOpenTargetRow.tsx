import type { JSX } from 'react'
import { useT } from '../../../contexts/LocaleContext'
import { useOpenTargetsStore } from '../../../stores/openTargetsStore'
import { openTargetLabel, renderOpenTargetIcon, useOpenTargets } from '../../../utils/openTargets'
import { SettingsRow } from '../SettingsGroup'
import { SettingsSelect } from '../ui/SettingsSelect'

export function DefaultOpenTargetRow({ width }: { width: string }): JSX.Element {
  const t = useT()
  const view = useOpenTargets(null)
  const current = view.effective
  const options = view.targets.length > 0
    ? view.targets.map((target) => ({
        value: target.id,
        label: openTargetLabel(target, t),
        icon: renderOpenTargetIcon(target)
      }))
    : [{
        value: '',
        label: t(view.status === 'idle' || view.status === 'loading' ? 'openTargets.loading' : 'openTargets.noneFound'),
        disabled: true
      }]

  return (
    <SettingsRow
      label={t('settings.general.defaultOpenTarget')}
      description={t('settings.general.defaultOpenTargetHint')}
      htmlFor="settings-default-open-target"
      control={
        <SettingsSelect
          id="settings-default-open-target"
          value={current?.id ?? ''}
          disabled={view.targets.length === 0}
          onValueChange={(targetId) => { useOpenTargetsStore.getState().setGlobal(targetId) }}
          style={{ width }}
          menuMaxHeight={320}
          options={options}
        />
      }
    />
  )
}

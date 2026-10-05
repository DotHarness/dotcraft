import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { useConfigSetting } from '../../../../stores/configStore'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SettingsSelect } from '../../ui/SettingsSelect'

const SELECT_ID = 'settings-code-mode'

export function CodeModeSettingsGroup(): JSX.Element {
  const t = useT()
  const setting = useConfigSetting('Tools.CodeMode.Mode', (error) => t('settings.codeMode.saveFailed', { error }))
  const mode = typeof setting.value === 'string' ? setting.value.toLowerCase() : ''
  return (
    <SettingsGroup title={t('settings.group.tools')}>
      <SettingsRow
        label={t('settings.codeMode.label')}
        description={t('settings.codeMode.description')}
        htmlFor={SELECT_ID}
        control={
          <SettingsSelect
            id={SELECT_ID}
            value={mode}
            disabled={setting.pending}
            ariaLabel={t('settings.codeMode.label')}
            style={{ width: '240px' }}
            onValueChange={(next) => {
              if (next !== mode) void setting.set(next)
            }}
            options={[
              {
                value: 'off',
                label: t('settings.codeMode.off.label'),
                description: t('settings.codeMode.off.description')
              },
              {
                value: 'on',
                label: t('settings.codeMode.on.label'),
                description: t('settings.codeMode.on.description')
              },
              {
                value: 'only',
                label: t('settings.codeMode.only.label'),
                description: t('settings.codeMode.only.description')
              }
            ]}
          />
        }
      />
    </SettingsGroup>
  )
}

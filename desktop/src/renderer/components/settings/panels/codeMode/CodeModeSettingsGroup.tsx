import type { JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { SettingsSelect } from '../../ui/SettingsSelect'
import type { CodeModeMode, CodeModeSettings } from './useCodeModeSettings'

const SELECT_ID = 'settings-code-mode'

export function CodeModeSettingsGroup({ settings }: { settings: CodeModeSettings }): JSX.Element {
  const t = useT()
  return (
    <SettingsGroup title={t('settings.group.tools')}>
      <SettingsRow
        label={t('settings.codeMode.label')}
        description={t('settings.codeMode.description')}
        htmlFor={SELECT_ID}
        control={
          <SettingsSelect
            id={SELECT_ID}
            value={settings.mode}
            disabled={settings.pending}
            ariaLabel={t('settings.codeMode.label')}
            style={{ width: '240px' }}
            onValueChange={(next) => void settings.setMode(next as CodeModeMode)}
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

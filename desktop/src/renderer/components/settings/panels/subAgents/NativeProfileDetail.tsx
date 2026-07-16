import { type JSX } from 'react'
import { useT } from '../../../../contexts/LocaleContext'
import { SettingsGroup, SettingsRow } from '../../SettingsGroup'
import { PillSwitch } from '../../../ui/PillSwitch'
import { AgentIcon } from './AgentIcon'
import { SettingsBreadcrumb } from '../../SettingsBreadcrumb'
import type { SubAgentProfileEntryWire } from './wire'
import {
  pageDescriptionStyle,
  pageHeadingStyle,
  pageStyle
} from './styles'

interface NativeProfileDetailProps {
  profile: SubAgentProfileEntryWire
  onBack: () => void
}

export function NativeProfileDetail({ profile, onBack }: NativeProfileDetailProps): JSX.Element {
  const t = useT()

  return (
    <div style={pageStyle()}>
      <SettingsBreadcrumb
        parentLabel={t('settings.subAgents.title')}
        currentLabel={t('settings.subAgents.preset.native.title')}
        onBack={onBack}
      />

      <div style={{ display: 'flex', alignItems: 'center', gap: '14px' }}>
        <AgentIcon name={profile.name} isBuiltIn size={40} />
        <div style={{ minWidth: 0 }}>
          <div style={pageHeadingStyle()}>{t('settings.subAgents.preset.native.title')}</div>
          <div style={pageDescriptionStyle()}>
            {t('settings.subAgents.preset.native.description')}
          </div>
        </div>
      </div>

      <SettingsGroup>
        <SettingsRow
          label={t('settings.subAgents.preset.enableTitle')}
          description={t('settings.subAgents.preset.nativeLockedHint')}
          control={
            <PillSwitch
              aria-label={t('settings.subAgents.toggleAria', { name: profile.name })}
              checked
              onChange={() => {
                /* native is always enabled */
              }}
              disabled
            />
          }
        />
      </SettingsGroup>
    </div>
  )
}

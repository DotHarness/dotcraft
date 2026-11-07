import type { JSX, ReactNode } from 'react'

import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import { SettingsDescriptionWithLearnMore } from './SettingsLearnMoreLink'
import { SettingsPageHeader } from './SettingsPageHeader'
import { SegmentedControl } from './ui/SegmentedControl'

type PersonalizationSegment = 'personalization' | 'subAgents'

export function PersonalizationPanelShell({
  segment,
  children
}: {
  segment: PersonalizationSegment
  children: ReactNode
}): JSX.Element {
  const t = useT()
  const subAgentEnabled = useConnectionStore((s) => s.capabilities?.subAgentManagement === true)
  const setActiveSettingsTab = useUIStore((s) => s.setActiveSettingsTab)
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
      <SettingsPageHeader
        title={t('settings.tab.personalization')}
        description={
          <SettingsDescriptionWithLearnMore topic="memory" aboutKey="settings.tab.personalization">
            {t('settings.personalization.description')}
          </SettingsDescriptionWithLearnMore>
        }
      />
      {subAgentEnabled && (
        <div>
          <SegmentedControl<PersonalizationSegment>
            ariaLabel={t('settings.tab.personalization')}
            value={segment}
            options={[
              { value: 'personalization', label: t('settings.tab.general') },
              { value: 'subAgents', label: t('settings.tab.subAgents') }
            ]}
            onChange={setActiveSettingsTab}
          />
        </div>
      )}
      {children}
    </div>
  )
}

import type { JSX, ReactNode } from 'react'

import { useT } from '../../contexts/LocaleContext'
import { SettingsDescriptionWithLearnMore } from './SettingsLearnMoreLink'
import { SettingsPageHeader } from './SettingsPageHeader'

export function PersonalizationPanelShell({
  children
}: {
  children: ReactNode
}): JSX.Element {
  const t = useT()
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
      {children}
    </div>
  )
}

import type { JSX, ReactNode } from 'react'
import { SettingsPageHeader } from './SettingsPageHeader'

interface SettingsPanelShellProps {
  title: ReactNode
  description?: ReactNode
  action?: ReactNode
  headerChildren?: ReactNode
  children: ReactNode
}

export function SettingsPanelShell({
  title,
  description,
  action,
  headerChildren,
  children
}: SettingsPanelShellProps): JSX.Element {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
      <SettingsPageHeader title={title} description={description} action={action}>
        {headerChildren}
      </SettingsPageHeader>
      {children}
    </div>
  )
}

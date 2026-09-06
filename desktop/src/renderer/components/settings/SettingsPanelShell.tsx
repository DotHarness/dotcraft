import type { JSX, ReactNode } from 'react'
import { SettingsPageHeader } from './SettingsPageHeader'

interface SettingsPanelShellProps {
  title: ReactNode
  description?: ReactNode
  action?: ReactNode
  breadcrumb?: ReactNode
  headerChildren?: ReactNode
  children: ReactNode
}

export function SettingsPanelShell({
  title,
  description,
  action,
  breadcrumb,
  headerChildren,
  children
}: SettingsPanelShellProps): JSX.Element {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '20px' }}>
      <SettingsPageHeader title={title} description={description} action={action} breadcrumb={breadcrumb}>
        {headerChildren}
      </SettingsPageHeader>
      {children}
    </div>
  )
}

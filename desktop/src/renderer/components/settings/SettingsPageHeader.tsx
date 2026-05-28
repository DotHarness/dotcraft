import type { CSSProperties, JSX, ReactNode } from 'react'

interface SettingsPageHeaderProps {
  title: ReactNode
  description?: ReactNode
  action?: ReactNode
  children?: ReactNode
}

export function SettingsPageHeader({
  title,
  description,
  action,
  children
}: SettingsPageHeaderProps): JSX.Element {
  return (
    <div style={settingsPageHeaderStyle()}>
      <div style={settingsPageHeaderTextStyle()}>
        <div style={settingsPageTitleStyle()}>{title}</div>
        {description && <div style={settingsPageDescriptionStyle()}>{description}</div>}
        {children}
      </div>
      {action && <div style={settingsPageHeaderActionStyle()}>{action}</div>}
    </div>
  )
}

export function settingsPageHeaderStyle(): CSSProperties {
  return {
    display: 'flex',
    justifyContent: 'space-between',
    alignItems: 'center',
    gap: '12px'
  }
}

export function settingsPageHeaderTextStyle(): CSSProperties {
  return {
    minWidth: 0
  }
}

export function settingsPageHeaderActionStyle(): CSSProperties {
  return {
    flexShrink: 0
  }
}

export function settingsPageTitleStyle(): CSSProperties {
  return {
    fontSize: '18px',
    fontWeight: 600,
    color: 'var(--text-primary)',
    lineHeight: 1.25
  }
}

export function settingsPageDescriptionStyle(): CSSProperties {
  return {
    fontSize: '12px',
    color: 'var(--text-dimmed)',
    lineHeight: 1.5,
    marginTop: '4px'
  }
}

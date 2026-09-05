import type { JSX, ReactNode } from 'react'

import { emptyBox } from '../servers/serversStyles'

/** First-run state for a Connections segment whose backing capability is not set up yet. */
export function SegmentSetupState({
  icon,
  title,
  description,
  action
}: {
  icon: ReactNode
  title: string
  description: string
  action?: ReactNode
}): JSX.Element {
  return (
    <div style={emptyBox}>
      <span
        style={{
          display: 'inline-flex',
          width: 44,
          height: 44,
          alignItems: 'center',
          justifyContent: 'center',
          borderRadius: 12,
          background: 'var(--bg-tertiary)',
          color: 'var(--text-secondary)',
          marginBottom: 6
        }}
        aria-hidden
      >
        {icon}
      </span>
      <div style={{ fontSize: 14, fontWeight: 600 }}>{title}</div>
      <div style={{ maxWidth: '44ch', color: 'var(--text-secondary)', fontSize: 12.5 }}>{description}</div>
      {action && <div style={{ marginTop: 8 }}>{action}</div>}
    </div>
  )
}

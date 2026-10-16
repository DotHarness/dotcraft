import type { CSSProperties, JSX } from 'react'
import { Skeleton } from '../ui/Skeleton'
import { settingsContentContainerStyle, settingsMainStyle } from './settingsLayout'
import { SETTINGS_SURFACE_CLASS } from './settingsTypography'

const GROUP_ROWS = [3, 2] as const

export function SettingsPanelSkeleton(): JSX.Element {
  return (
    <div aria-hidden="true" style={panelStyle}>
      <div style={headerStyle}>
        <Skeleton width={160} height={22} radius={6} />
        <Skeleton width="55%" height={12} />
      </div>
      {GROUP_ROWS.map((rows, group) => (
        <div key={group} style={groupStyle}>
          <Skeleton width={120} height={14} />
          <div style={cardStyle}>
            {Array.from({ length: rows }, (_, row) => (
              <div key={row} style={row === 0 ? rowStyle : { ...rowStyle, ...rowDividerStyle }}>
                <div style={rowTextStyle}>
                  <Skeleton width="40%" height={12} />
                  <Skeleton width="65%" height={10} />
                </div>
                <Skeleton width={72} height={24} radius={6} />
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  )
}

export function SettingsViewSkeleton(): JSX.Element {
  return (
    <div aria-busy="true" style={viewStyle}>
      <main className="dc-scrollbar-stable" style={settingsMainStyle()}>
        <div className={SETTINGS_SURFACE_CLASS} style={settingsContentContainerStyle()}>
          <SettingsPanelSkeleton />
        </div>
      </main>
    </div>
  )
}

const viewStyle: CSSProperties = { display: 'flex', flexDirection: 'column', height: '100%', minHeight: 0 }
const panelStyle: CSSProperties = { display: 'flex', flexDirection: 'column', gap: '20px' }
const headerStyle: CSSProperties = { display: 'flex', flexDirection: 'column', gap: '8px' }
const groupStyle: CSSProperties = { display: 'flex', flexDirection: 'column', gap: '8px' }
const cardStyle: CSSProperties = {
  border: '1px solid var(--border-default)',
  borderRadius: '12px',
  background: 'var(--bg-secondary)',
  overflow: 'hidden'
}
const rowStyle: CSSProperties = { display: 'flex', alignItems: 'center', gap: '12px', padding: '14px 16px' }
const rowDividerStyle: CSSProperties = { borderTop: '1px solid var(--border-default)' }
const rowTextStyle: CSSProperties = { flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', gap: '6px' }

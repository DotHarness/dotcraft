import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { ConnectionStatusIndicator } from '../ConnectionStatusIndicator'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from './sidebarNavRowStyles'
import { SettingsIcon } from '../ui/AppIcons'
import { ShortcutBadge } from '../ui/ShortcutBadge'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

/**
 * Sidebar footer: a single Settings row with an ambient connection-status dot at
 * the right. The dot stays quiet while connected and turns colored/pulsing when
 * connecting, disconnected, or in error. App version and the "What's New" entry
 * live under Settings → General.
 * Spec §9.6
 */
export function SidebarFooter(): JSX.Element {
  const t = useT()
  const { activeMainView, setActiveMainView } = useUIStore()
  const settingsActive = activeMainView === 'settings'
  const [settingsRowActive, setSettingsRowActive] = useState(false)
  const settingsVisualActive = settingsActive || settingsRowActive
  return (
    <div
      style={{
        marginTop: '8px',
        padding: '8px 0',
        flexShrink: 0
      }}
    >
      <button
        type="button"
        onClick={() => setActiveMainView('settings')}
        aria-label={t('sidebar.openSettingsAria')}
        onFocus={() => setSettingsRowActive(true)}
        onBlur={() => setSettingsRowActive(false)}
        onMouseEnter={() => setSettingsRowActive(true)}
        onMouseLeave={() => setSettingsRowActive(false)}
        style={{
          ...SIDEBAR_NAV_ROW_OUTER,
          ...SIDEBAR_NAV_BORDER_INACTIVE,
          background: settingsActive
            ? 'var(--sidebar-control-active)'
            : settingsRowActive
              ? 'var(--sidebar-control-hover)'
              : 'transparent',
          color: settingsVisualActive ? 'var(--text-primary)' : 'var(--text-secondary)',
          cursor: 'pointer',
          justifyContent: 'space-between',
          transition: 'background-color 120ms ease, color 120ms ease'
        }}
      >
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
          <span style={SIDEBAR_NAV_ICON_SLOT}>
            <span style={{ display: 'block', flexShrink: 0 }}>
              <SettingsIcon />
            </span>
          </span>
          <span style={{ ...SIDEBAR_NAV_LABEL, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {t('sidebarFooter.settings')}
          </span>
        </span>
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', flexShrink: 0 }}>
          {settingsRowActive && <ShortcutBadge shortcut={ACTION_SHORTCUTS.settings} />}
          <ConnectionStatusIndicator variant="dot" />
        </span>
      </button>
    </div>
  )
}

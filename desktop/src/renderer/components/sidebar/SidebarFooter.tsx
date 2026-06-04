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
import { ActionTooltip } from '../ui/ActionTooltip'
import { ShortcutBadge } from '../ui/ShortcutBadge'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'

const APP_VERSION = typeof __APP_VERSION__ !== 'undefined' ? __APP_VERSION__ : '0.0.0'

/**
 * Sidebar footer showing settings button, connection status and app version.
 * Spec §9.6
 */
export function SidebarFooter(): JSX.Element {
  const t = useT()
  const { activeMainView, setActiveMainView, requestOpenWhatsNew } = useUIStore()
  const settingsActive = activeMainView === 'settings'
  const [settingsRowActive, setSettingsRowActive] = useState(false)
  const settingsVisualActive = settingsActive || settingsRowActive
  return (
    <div
      style={{
        marginTop: '8px',
        padding: '8px 0',
        flexShrink: 0,
        display: 'flex',
        flexDirection: 'column',
        gap: '2px'
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
        {settingsRowActive && <ShortcutBadge shortcut={ACTION_SHORTCUTS.settings} />}
      </button>

      <div
        style={{
          ...SIDEBAR_NAV_ROW_OUTER,
          ...SIDEBAR_NAV_BORDER_INACTIVE,
          backgroundColor: 'transparent',
          cursor: 'default',
          justifyContent: 'space-between',
          gap: '8px'
        }}
      >
        <ConnectionStatusIndicator />
        <ActionTooltip label={t('whatsNew.open')} placement="top">
          <button
            type="button"
            onClick={requestOpenWhatsNew}
            aria-label={t('whatsNew.open')}
            style={{
              border: 'none',
              borderRadius: 4,
              background: 'transparent',
              color: 'var(--text-dimmed)',
              cursor: 'pointer',
              flexShrink: 0,
              fontSize: 'var(--type-secondary-size)',
              lineHeight: 'var(--type-secondary-line-height)',
              padding: '2px 4px'
            }}
            onMouseEnter={(e) => {
              e.currentTarget.style.backgroundColor = 'var(--sidebar-control-hover)'
              e.currentTarget.style.color = 'var(--text-primary)'
            }}
            onMouseLeave={(e) => {
              e.currentTarget.style.backgroundColor = 'transparent'
              e.currentTarget.style.color = 'var(--text-dimmed)'
            }}
          >
            v{APP_VERSION}
          </button>
        </ActionTooltip>
      </div>
    </div>
  )
}

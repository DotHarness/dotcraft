import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useUIStore } from '../../stores/uiStore'
import { ShortcutBadge } from '../ui/ShortcutBadge'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { SidebarNavIcon } from './SidebarNavIcon'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from './sidebarNavRowStyles'

/** The matching Ctrl+N shortcut is registered globally in App.tsx. */
export function NewThreadButton(): JSX.Element {
  const t = useT()
  const goToNewChat = useUIStore((s) => s.goToNewChat)
  const [active, setActive] = useState(false)

  return (
    <div style={{ padding: '8px 0 0', flexShrink: 0 }}>
      <button
        className="dotcraft-sidebar-row-radius"
        data-nav-icon-host=""
        onClick={() => goToNewChat()}
        aria-label={t('sidebar.newThread')}
        onFocus={() => setActive(true)}
        onBlur={() => setActive(false)}
        style={{
          ...SIDEBAR_NAV_ROW_OUTER,
          ...SIDEBAR_NAV_BORDER_INACTIVE,
          backgroundColor: active ? 'var(--sidebar-control-hover)' : 'transparent',
          color: 'var(--text-primary)',
          borderRadius: 'var(--sidebar-row-radius)',
          cursor: 'pointer',
          justifyContent: 'space-between',
          transition: 'background-color 120ms ease, color 120ms ease'
        }}
        onMouseEnter={() => {
          setActive(true)
        }}
        onMouseLeave={() => {
          setActive(false)
        }}
      >
        <span style={{ display: 'inline-flex', alignItems: 'center', gap: '8px', minWidth: 0 }}>
          <span style={SIDEBAR_NAV_ICON_SLOT}>
            <SidebarNavIcon name="new-chat" />
          </span>
          <span style={{ ...SIDEBAR_NAV_LABEL, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {t('sidebar.newThreadLabel')}
          </span>
        </span>
        {active && (
          <ShortcutBadge
            shortcut={ACTION_SHORTCUTS.newThread}
          />
        )}
      </button>
    </div>
  )
}

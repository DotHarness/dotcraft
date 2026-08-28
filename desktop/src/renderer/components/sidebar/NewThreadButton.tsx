import { useState } from 'react'
import { useT } from '../../contexts/LocaleContext'
import { useConnectionStore } from '../../stores/connectionStore'
import { useUIStore } from '../../stores/uiStore'
import { ShortcutBadge } from '../ui/ShortcutBadge'
import { ACTION_SHORTCUTS } from '../ui/shortcutKeys'
import { SquarePen } from 'lucide-react'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_LABEL,
  SIDEBAR_NAV_ROW_OUTER
} from './sidebarNavRowStyles'

/** The matching Ctrl+N shortcut is registered globally in App.tsx. */
export function NewThreadButton(): JSX.Element {
  const t = useT()
  const status = useConnectionStore((s) => s.status)
  const goToNewChat = useUIStore((s) => s.goToNewChat)
  const [active, setActive] = useState(false)

  const isConnected = status === 'connected'
  const showShortcut = isConnected && active

  function handleClick(): void {
    if (!isConnected) return
    goToNewChat()
  }

  return (
    <div style={{ padding: '8px 0 0', flexShrink: 0 }}>
      <button
        className="dotcraft-sidebar-row-radius"
        onClick={handleClick}
        disabled={!isConnected}
        aria-label={t('sidebar.newThread')}
        onFocus={() => setActive(true)}
        onBlur={() => setActive(false)}
        style={{
          ...SIDEBAR_NAV_ROW_OUTER,
          ...SIDEBAR_NAV_BORDER_INACTIVE,
          backgroundColor: active && isConnected ? 'var(--sidebar-control-hover)' : 'transparent',
          color: !isConnected
            ? 'var(--text-tertiary)'
            : 'var(--text-primary)',
          borderRadius: 'var(--sidebar-row-radius)',
          cursor: isConnected ? 'pointer' : 'default',
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
            <SquarePen size={16} strokeWidth={1.8} aria-hidden="true" style={{ display: 'block' }} />
          </span>
          <span style={{ ...SIDEBAR_NAV_LABEL, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
            {t('sidebar.newThreadLabel')}
          </span>
        </span>
        {showShortcut && (
          <ShortcutBadge
            shortcut={ACTION_SHORTCUTS.newThread}
          />
        )}
      </button>
    </div>
  )
}

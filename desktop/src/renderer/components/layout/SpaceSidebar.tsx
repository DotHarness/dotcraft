import type { CSSProperties, JSX } from 'react'
import { ArrowLeft } from 'lucide-react'
import { useT } from '../../contexts/LocaleContext'
import { leaveSpace, type ActiveSpace } from '../../stores/spaceStore'
import { DesktopPluginContributionBoundary } from '../desktopPlugins/DesktopPluginContributionBoundary'
import {
  SIDEBAR_NAV_BORDER_INACTIVE,
  SIDEBAR_NAV_ICON_SLOT,
  SIDEBAR_NAV_ROW_OUTER
} from '../sidebar/sidebarNavRowStyles'

export function SpaceSidebar({ active }: { active: ActiveSpace }): JSX.Element {
  const t = useT()
  const { space, location } = active
  const Sidebar = space.sidebar
  return (
    <div style={containerStyle} data-testid="space-sidebar">
      <button
        className="dotcraft-sidebar-nav-button dotcraft-sidebar-row-radius"
        type="button"
        onClick={() => void leaveSpace()}
        style={backRowStyle}
        aria-label={t('common.backToApp')}
      >
        <span style={SIDEBAR_NAV_ICON_SLOT}>
          <ArrowLeft size={16} strokeWidth={2} aria-hidden="true" />
        </span>
        <span style={labelStyle}>{t('common.backToApp')}</span>
      </button>
      <nav style={navStyle}>
        <DesktopPluginContributionBoundary identity={`${space.pluginId}/${space.id}`}>
          <Sidebar host={space.host} contributionId={space.id} location={location} />
        </DesktopPluginContributionBoundary>
      </nav>
    </div>
  )
}

const containerStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  height: '100%',
  minHeight: 0,
  padding: '4px 0 10px',
  gap: '4px',
  overflow: 'hidden'
}

const navStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  minHeight: 0,
  marginTop: '8px',
  overflowY: 'auto'
}

const backRowStyle: CSSProperties = {
  ...SIDEBAR_NAV_ROW_OUTER,
  ...SIDEBAR_NAV_BORDER_INACTIVE,
  color: 'var(--text-secondary)'
}

const labelStyle: CSSProperties = {
  minWidth: 0,
  overflow: 'hidden',
  textOverflow: 'ellipsis',
  whiteSpace: 'nowrap'
}

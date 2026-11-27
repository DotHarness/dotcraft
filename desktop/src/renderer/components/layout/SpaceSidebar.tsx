import type { CSSProperties, JSX } from 'react'
import { useLocale } from '../../contexts/LocaleContext'
import { resolveDesktopPluginLabel } from '../../plugins/desktopPluginRegistry'
import type { ActiveSpace } from '../../stores/spaceStore'
import { DesktopPluginContributionBoundary } from '../desktopPlugins/DesktopPluginContributionBoundary'
import { SIDEBAR_RAIL_CONTENT_INSET } from '../sidebar/sidebarNavRowStyles'

export function SpaceSidebar({ active }: { active: ActiveSpace }): JSX.Element {
  const locale = useLocale()
  const { space, location } = active
  const Sidebar = space.sidebar
  const heading = resolveDesktopPluginLabel(space.label, locale)

  return (
    <div style={containerStyle} data-testid="space-sidebar">
      <div style={headingStyle}>{heading}</div>
      <nav style={navStyle} aria-label={heading}>
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
  flex: 1,
  minHeight: 0,
  paddingTop: '8px',
  borderTop: '1px solid var(--border-subtle)',
  overflow: 'hidden'
}

const headingStyle: CSSProperties = {
  padding: `2px 12px 4px ${SIDEBAR_RAIL_CONTENT_INSET}`,
  fontSize: 'var(--type-secondary-size)',
  lineHeight: 'var(--type-secondary-line-height)',
  fontWeight: 500,
  color: 'var(--text-dimmed)',
  flexShrink: 0,
  userSelect: 'none'
}

const navStyle: CSSProperties = {
  display: 'flex',
  flexDirection: 'column',
  flex: 1,
  minHeight: 0,
  overflowY: 'auto',
  paddingBottom: '8px'
}

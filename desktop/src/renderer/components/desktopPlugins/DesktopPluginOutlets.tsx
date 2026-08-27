import type {
  ActiveDesktopPluginMainView,
  ActiveDesktopPluginSettingsPage
} from '../../plugins/desktopPluginRegistry'
import { DesktopPluginContributionBoundary } from './DesktopPluginContributionBoundary'

export function DesktopPluginMainViewOutlet({
  contribution
}: {
  contribution: ActiveDesktopPluginMainView
}): JSX.Element {
  const Component = contribution.component
  return (
    <DesktopPluginContributionBoundary
      key={`${contribution.viewKey}:${contribution.revision}`}
      identity={contribution.viewKey}
    >
      <Component host={contribution.host} contributionId={contribution.id} />
    </DesktopPluginContributionBoundary>
  )
}

export function DesktopPluginSettingsPageOutlet({
  contribution
}: {
  contribution: ActiveDesktopPluginSettingsPage
}): JSX.Element {
  const Component = contribution.component
  return (
    <DesktopPluginContributionBoundary
      key={`${contribution.settingsKey}:${contribution.revision}`}
      identity={contribution.settingsKey}
    >
      <Component host={contribution.host} contributionId={contribution.id} />
    </DesktopPluginContributionBoundary>
  )
}

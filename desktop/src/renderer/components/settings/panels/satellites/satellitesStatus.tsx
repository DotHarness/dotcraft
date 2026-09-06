import type { JSX, ReactNode } from 'react'

import type { SatelliteState } from '../../../../../shared/satellites'
import { StatusIndicator, statusTextStyle, type StatusTone } from '../settingsStatusStyles'

export const SATELLITE_STATE_KEY: Record<SatelliteState, string> = {
  offline: 'settings.satellites.state.offline',
  ready: 'settings.satellites.state.ready',
  inUse: 'settings.satellites.state.inUse'
}

/** Only a machine actually running work earns a hue; ready and offline stay quiet. */
export const SATELLITE_TONE: Record<SatelliteState, StatusTone> = {
  offline: 'neutral',
  ready: 'neutral',
  inUse: 'success'
}

export function SatelliteStatusText({
  state,
  children
}: {
  state: SatelliteState
  children: ReactNode
}): JSX.Element {
  return (
    <span style={statusTextStyle()}>
      <StatusIndicator tone={SATELLITE_TONE[state]} />
      {children}
    </span>
  )
}

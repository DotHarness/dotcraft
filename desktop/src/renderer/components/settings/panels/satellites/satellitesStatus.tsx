import type { CSSProperties, JSX, ReactNode } from 'react'

import type { SatelliteState } from '../../../../../shared/satellites'
import type { StatusMenuTone } from '../../../ui/StatusMenuButton'
import { dotStyle, statusTextStyle } from '../settingsStatusStyles'

export const SATELLITE_STATE_KEY: Record<SatelliteState, string> = {
  offline: 'settings.satellites.state.offline',
  ready: 'settings.satellites.state.ready',
  inUse: 'settings.satellites.state.inUse'
}

/** Only a machine actually running work earns a hue; ready and offline stay neutral. */
export const SATELLITE_MENU_TONE: Record<SatelliteState, StatusMenuTone> = {
  offline: 'neutral',
  ready: 'neutral',
  inUse: 'success'
}

// The hue lives in the dot; the label stays neutral text.
function stateDotStyle(state: SatelliteState): CSSProperties {
  if (state === 'ready') return dotStyle('neutral')
  return {
    ...dotStyle('success'),
    ...(state === 'offline' ? { background: 'var(--text-dimmed)' } : null)
  }
}

function stateLabelStyle(state: SatelliteState): CSSProperties {
  return {
    ...statusTextStyle('neutral'),
    ...(state === 'offline' ? { color: 'var(--text-dimmed)' } : null)
  }
}

export function SatelliteStatusText({
  state,
  children
}: {
  state: SatelliteState
  children: ReactNode
}): JSX.Element {
  return (
    <span style={stateLabelStyle(state)}>
      <span style={stateDotStyle(state)} />
      {children}
    </span>
  )
}

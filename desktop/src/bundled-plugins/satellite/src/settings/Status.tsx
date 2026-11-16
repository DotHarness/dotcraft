import type { JSX, ReactNode } from 'react'
import type { SatelliteState } from '../types'

export const SATELLITE_STATE_KEY: Record<SatelliteState, string> = {
  offline: 'settings.satellites.state.offline', ready: 'settings.satellites.state.ready', inUse: 'settings.satellites.state.inUse'
}

export function SatelliteStatusText({ state, children }: { state: SatelliteState; children: ReactNode }): JSX.Element {
  return <span className="satellite-status" data-active={state === 'inUse'}><span className="satellite-status-dot" aria-hidden />{children}</span>
}

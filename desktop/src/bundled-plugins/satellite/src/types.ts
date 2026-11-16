export type {
  DesktopPluginSatellite as Satellite,
  DesktopPluginSatelliteEvent as SatelliteEvent,
  DesktopPluginSatelliteInvite as SatelliteInvite,
  DesktopPluginSatelliteWorkspace as SatelliteWorkspace,
  DesktopPluginSharePcStatus as SharePcStatus,
  DesktopPluginLocale as AppLocale
} from '@dotcraft/plugin'

export type SatelliteState = 'offline' | 'ready' | 'inUse'

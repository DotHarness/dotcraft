import type { DesktopPluginActivate } from '@dotcraft/plugin'
import { SatelliteDish } from 'lucide-react'
import { installSatelliteHost } from './runtime'
import { startSatellites, useSatellitesStore } from './state'
import { SatelliteSettings } from './settings/SatelliteSettings'
import { RunOnPicker } from './composer/RunOnPicker'
import { SatelliteToolRenderer } from './tool-renderers/SatelliteToolRenderer'
import { ScreenViewHeaderSlot } from './screen-view/ScreenViewHeaderSlot'
import { disposeScreenView } from './screen-view/screenViewStore'
import { announceSatelliteArrival } from './notices'

const label = { default: 'Satellites', translations: { 'zh-Hans': '卫星', ja: 'サテライト', ko: '위성', es: 'Satélites', fr: 'Satellites', de: 'Satelliten' } }

export const activate: DesktopPluginActivate = (host) => {
  host.effect(() => {
    let active = true
    const uninstall = installSatelliteHost(host)
    const stopFleet = startSatellites(host, (event) => { void announceSatelliteArrival(host, event, () => active) })
    const updateLeases = (): void => {
      const route = host.remoteToolHost.getSnapshot()
      useSatellitesStore.getState().applyBusy(route.supported && !route.remote ? route.hosts : [])
    }
    updateLeases()
    const stopRoutes = host.remoteToolHost.onChange(updateLeases)
    return () => {
      active = false
      stopRoutes()
      stopFleet()
      disposeScreenView()
      uninstall()
    }
  })
  host.ui.add('composer.status.workspace.run-on', ({ context }) => <RunOnPicker context={context} />)
  host.ui.add('thread.header.actions', ({ context }) => <ScreenViewHeaderSlot threadId={context.threadId} />)
  return {
    settingsPages: [{ id: 'satellites', label, icon: SatelliteDish, order: 46, component: SatelliteSettings }],
    toolRenderers: [{ id: 'remote-tool-host', presentationId: 'core.remote-tool-host', component: SatelliteToolRenderer }]
  }
}

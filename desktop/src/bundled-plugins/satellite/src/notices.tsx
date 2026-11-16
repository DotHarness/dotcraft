import { Monitor } from 'lucide-react'
import type { DesktopPluginHost, DesktopPluginSatelliteEvent } from '@dotcraft/plugin'
import { translate } from './i18n'

export async function announceSatelliteArrival(host: DesktopPluginHost, event: DesktopPluginSatelliteEvent, active: () => boolean): Promise<void> {
  if (event.kind !== 'joined' || !event.inviteId) return
  try {
    if (!(await host.satellites.wasInvitedHere(event.inviteId)) || !active()) return
    const locale = host.environment.locale
    const satellite = event.satellite
    const name = satellite?.displayName ?? event.peerId
    const user = (satellite?.userName ?? name).replace(/^./, (first) => first.toUpperCase())
    const threadId = host.session.threadId
    const workspaceId = satellite?.connected ? satellite.workspaces.find((workspace) => !workspace.busy)?.workspaceId : undefined
    const route = host.remoteToolHost.getSnapshot()
    host.ui.showToast({
      tone: 'neutral',
      message: translate(locale, 'satellite.toast.joined.message', { name }),
      description: translate(locale, 'satellite.toast.joined.description', { user }),
      icon: <Monitor size={16} strokeWidth={2.2} aria-hidden />,
      ...(threadId && workspaceId && route.supported && !route.remote ? { action: {
        label: translate(locale, 'satellite.toast.joined.action'),
        run: () => { if (active()) void host.remoteToolHost.connect(threadId, event.peerId, workspaceId).catch(() => {}) }
      } } : {})
    })
  } catch { }
}

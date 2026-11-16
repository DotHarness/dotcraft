import { expect, it, vi } from 'vitest'
import type { DesktopPluginHost, DesktopPluginSatelliteEvent } from '@dotcraft/plugin'
import { announceSatelliteArrival } from './notices'

const event: DesktopPluginSatelliteEvent = {
  kind: 'joined', at: '2026-11-16T00:00:00Z', peerId: 'peer', inviteId: 'invitation',
  satellite: { peerId: 'peer', hostId: 'peer', displayName: 'Test PC', connected: true, capabilities: [], workspaces: [{ workspaceId: 'work', path: 'fixture-workspace', busy: false }] }
}

it('does not publish an arrival notification after its generation is disposed', async () => {
  let resolve!: (invited: boolean) => void
  const ownership = new Promise<boolean>((done) => { resolve = done })
  const showToast = vi.fn()
  const host = { satellites: { wasInvitedHere: () => ownership }, ui: { showToast } } as unknown as DesktopPluginHost
  let active = true
  const pending = announceSatelliteArrival(host, event, () => active)
  active = false
  resolve(true)
  await pending
  expect(showToast).not.toHaveBeenCalled()
})

it('offers routing only for an owned invitation and a locally routable current thread', async () => {
  const connect = vi.fn().mockResolvedValue(undefined)
  const showToast = vi.fn()
  const host = {
    satellites: { wasInvitedHere: vi.fn().mockResolvedValue(true) },
    environment: { locale: 'en' }, session: { threadId: 'thread' }, ui: { showToast },
    remoteToolHost: { getSnapshot: () => ({ supported: true, remote: false }), connect }
  } as unknown as DesktopPluginHost
  await announceSatelliteArrival(host, event, () => true)
  showToast.mock.calls[0][0].action.run()
  expect(connect).toHaveBeenCalledWith('thread', 'peer', 'work')
  vi.mocked(host.satellites.wasInvitedHere).mockResolvedValue(false)
  await announceSatelliteArrival(host, event, () => true)
  expect(showToast).toHaveBeenCalledTimes(1)
})

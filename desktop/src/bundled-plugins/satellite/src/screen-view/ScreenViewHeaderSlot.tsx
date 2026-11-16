import { useEffect, useRef } from 'react'
import { useRemoteToolHost } from '../runtime'
import { useSatellitesStore } from '../state'
import { useScreenViewStore } from './screenViewStore'
import { ScreenViewDock } from './ScreenViewDock'
import { ScreenViewLauncher, canWatchHost } from './ScreenViewLauncher'
import { ScreenViewTheater } from './ScreenViewTheater'
import { useScreenStream } from './useScreenStream'

export function ScreenViewHeaderSlot({ threadId }: { threadId: string }): JSX.Element | null {
  const routes = useRemoteToolHost()
  const route = routes.routes[threadId]
  const hostId = route?.hostId ?? null
  const satellite = useSatellitesStore((state) =>
    hostId ? state.satellites.find((candidate) => candidate.peerId === hostId) : undefined
  )
  const session = useScreenViewStore((state) => state.session)
  const viewThreadId = useScreenViewStore((state) => state.threadId)
  const mode = useScreenViewStore((state) => state.mode)
  const status = useScreenViewStore((state) => state.status)
  const toggle = useScreenViewStore((state) => state.toggle)
  const syncThread = useScreenViewStore((state) => state.syncThread)
  const loadDockGeometry = useScreenViewStore((state) => state.loadDockGeometry)
  const launcherRef = useRef<HTMLSpanElement | null>(null)
  const online = satellite?.connected === true && route?.status !== 'leaseLost'
  const capabilities = satellite?.capabilities ?? []
  const hostName = satellite?.displayName ?? hostId ?? ''
  const canOpen = !routes.remote && canWatchHost({ hostId, online, capabilities })

  useEffect(() => {
    if (hostId) void loadDockGeometry()
  }, [hostId, loadDockGeometry])

  useEffect(() => {
    syncThread(threadId, hostId, canOpen)
  }, [canOpen, hostId, syncThread, threadId])

  useEffect(() => () => {
    if (useScreenViewStore.getState().threadId === threadId) syncThread(null, null, false)
  }, [syncThread, threadId])

  const open = session != null && viewThreadId === threadId
  const stream = useScreenStream(open ? session : null, status)
  if (!hostId || (!canOpen && !open)) return null

  return (
    <>
      <ScreenViewLauncher
        hostId={hostId}
        hostName={hostName}
        online={online}
        capabilities={capabilities}
        open={open}
        live={open && stream.state.kind === 'live'}
        anchorRef={launcherRef}
        onToggle={() => toggle(threadId, hostId)}
      />
      {open && mode === 'dock' && <ScreenViewDock hostName={hostName} stream={stream} anchorRef={launcherRef} />}
      {open && mode === 'theater' && <ScreenViewTheater hostName={hostName} stream={stream} />}
    </>
  )
}

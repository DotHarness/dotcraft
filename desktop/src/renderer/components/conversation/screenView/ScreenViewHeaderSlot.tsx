import { useEffect, useRef } from 'react'
import { bootstrapSatellites, useSatellitesStore } from '../../../stores/satellitesStore'
import { useThreadRouteStore } from '../../../stores/threadRouteStore'
import { bootstrapScreenView, useScreenViewStore } from '../../../stores/screenViewStore'
import { ScreenViewDock } from './ScreenViewDock'
import { ScreenViewLauncher, canWatchHost } from './ScreenViewLauncher'
import { ScreenViewTheater } from './ScreenViewTheater'
import { useScreenStream } from './useScreenStream'

interface ScreenViewHeaderSlotProps {
  threadId: string
}

export function ScreenViewHeaderSlot({ threadId }: ScreenViewHeaderSlotProps): JSX.Element | null {
  const route = useThreadRouteStore((s) => s.routes[threadId])
  const hostId = route?.hostId ?? null
  const satellite = useSatellitesStore((s) =>
    hostId ? s.satellites.find((candidate) => candidate.peerId === hostId) : undefined
  )
  const viewId = useScreenViewStore((s) => s.viewId)
  const viewThreadId = useScreenViewStore((s) => s.threadId)
  const mode = useScreenViewStore((s) => s.mode)
  const status = useScreenViewStore((s) => s.status)
  const toggle = useScreenViewStore((s) => s.toggle)
  const syncThread = useScreenViewStore((s) => s.syncThread)
  const loadDockGeometry = useScreenViewStore((s) => s.loadDockGeometry)
  const launcherRef = useRef<HTMLButtonElement | null>(null)

  const online = satellite?.connected === true && route?.status !== 'leaseLost'
  const capabilities = satellite?.capabilities ?? []
  const hostName = satellite?.displayName ?? hostId ?? ''
  const canOpen = canWatchHost({ hostId, online, capabilities })

  useEffect(() => {
    if (!hostId) return
    bootstrapSatellites()
    bootstrapScreenView()
    void loadDockGeometry()
  }, [hostId, loadDockGeometry])

  // Route changes close the active view; a remembered mode reopens when the route is watchable.
  useEffect(() => {
    syncThread(threadId, hostId, canOpen)
  }, [canOpen, hostId, syncThread, threadId])

  const open = viewId != null && viewThreadId === threadId
  const stream = useScreenStream(open ? viewId : null, status)

  if (!hostId) return null
  if (!canOpen && !open) return null

  return (
    <>
      <ScreenViewLauncher
        hostId={hostId}
        hostName={hostName}
        online={online}
        capabilities={capabilities}
        open={open}
        live={open && stream.state.kind === 'live'}
        buttonRef={launcherRef}
        onToggle={() => toggle(threadId, hostId)}
      />
      {open && mode === 'dock' && (
        <ScreenViewDock hostName={hostName} stream={stream} anchorRef={launcherRef} />
      )}
      {open && mode === 'theater' && <ScreenViewTheater hostName={hostName} stream={stream} />}
    </>
  )
}

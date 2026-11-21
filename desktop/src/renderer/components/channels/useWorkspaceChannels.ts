import { useCallback, useEffect, useRef, useState } from 'react'
import { useConnectionStore } from '../../stores/connectionStore'
import { normalizeChannelName, type ChannelStatus } from './channelPluginModel'
import { channelRequest } from './channelPluginRequests'

interface WorkspaceChannelsSnapshot {
  epoch: number
  statuses: Map<string, ChannelStatus> | null
  statusError: boolean
}

export function useWorkspaceChannels(active: boolean) {
  const epoch = useConnectionStore((s) => s.connectionEpoch)
  const canReadStatus = useConnectionStore((s) => s.status === 'connected' && s.capabilities?.channelStatus === true)
  const [snapshot, setSnapshot] = useState<WorkspaceChannelsSnapshot | null>(null)
  const latestRequest = useRef(0)

  const refreshStatus = useCallback(async (): Promise<void> => {
    if (!active || !canReadStatus) return
    const request = ++latestRequest.current
    const current = () => request === latestRequest.current && useConnectionStore.getState().connectionEpoch === epoch
    try {
      const { channels } = await channelRequest('channel/status', {})
      if (current()) {
        setSnapshot({
          epoch,
          statuses: new Map(channels.map((entry) => [normalizeChannelName(entry.name), entry])),
          statusError: false
        })
      }
    } catch {
      if (current()) {
        setSnapshot((previous) => ({
          epoch,
          statuses: previous?.epoch === epoch ? previous.statuses : null,
          statusError: true
        }))
      }
    }
  }, [active, canReadStatus, epoch])

  useEffect(() => {
    if (!active || !canReadStatus) return
    let disposed = false
    let timer: ReturnType<typeof setTimeout> | undefined
    async function poll(): Promise<void> {
      await refreshStatus()
      if (!disposed) timer = setTimeout(() => void poll(), 2000)
    }
    void poll()
    const unsubscribe = window.api.appServer.onNotification((event) => {
      if (event.method === 'config/changed' && event.params.regions?.includes('channels')) void refreshStatus()
    })
    return () => {
      disposed = true
      latestRequest.current += 1
      clearTimeout(timer)
      unsubscribe()
    }
  }, [active, canReadStatus, refreshStatus])

  const currentSnapshot = canReadStatus && snapshot?.epoch === epoch ? snapshot : null
  return {
    statuses: currentSnapshot?.statuses ?? null,
    statusError: currentSnapshot?.statusError ?? false,
    loading: active && canReadStatus && currentSnapshot === null,
    refreshStatus
  }
}

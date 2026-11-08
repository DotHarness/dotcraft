import { create } from 'zustand'
import type { UsageStatisticsConnection } from '../../shared/usageStatistics'

interface UsageConnectionState {
  capable: boolean | null
}

export const useUsageConnectionStore = create<UsageConnectionState>(() => ({ capable: null }))

let resolving: Promise<UsageStatisticsConnection> | null = null

function resolveConnection(): Promise<UsageStatisticsConnection> {
  if (!resolving) {
    resolving = window.api.appServer.getUsageConnection().then((connection) => {
      useUsageConnectionStore.setState({ capable: connection.usageTelemetry })
      return connection
    }).finally(() => { resolving = null })
  }
  return resolving
}

export async function requestUsage<T>(method: string, params: object): Promise<T> {
  const connection = await resolveConnection()
  if (!connection.usageTelemetry) throw new Error('Usage statistics are unavailable.')
  return await window.api.appServer.sendConnectionRequest(
    connection.connectionKey,
    method as Parameters<typeof window.api.appServer.sendConnectionRequest>[1],
    params as never
  ) as T
}

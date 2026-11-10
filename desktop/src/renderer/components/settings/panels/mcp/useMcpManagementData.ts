import { useCallback, useEffect, useState } from 'react'
import { useConnectionStore } from '../../../../stores/connectionStore'
import { isCurrentMcpManagementScope, listMcpManagementStatuses } from '../../../../stores/mcpManagement'
import { useMcpStore, type McpServerConfigWire, type McpServerStatusWire } from '../../../../stores/mcpStore'

export function useMcpManagementData(enabled: boolean) {
  const { status, connectionKey, connectionId } = useConnectionStore()
  const catalogRevision = useMcpStore((state) => state.catalogRevision)
  const mcpStatuses = useMcpStore((state) => state.statuses)
  const [mcpServers, setMcpServers] = useState<McpServerConfigWire[]>([])
  const [mcpLoading, setMcpLoading] = useState(false)
  const [mcpError, setMcpError] = useState<string | null>(null)

  const reloadMcpServers = useCallback(async () => {
    if (!enabled || status !== 'connected' || !connectionKey || !connectionId) return
    const scope = { connectionKey, connectionId }
    if (!isCurrentMcpManagementScope(scope)) return
    const result = await window.api.appServer.sendConnectionRequest(connectionKey, 'mcp/list', {},
      undefined, { connectionId })
    if (isCurrentMcpManagementScope(scope)) setMcpServers((result.servers ?? []) as McpServerConfigWire[])
  }, [enabled, status, connectionKey, connectionId])

  const reloadMcpStatuses = useCallback(async () => {
    if (!enabled || status !== 'connected' || !connectionKey || !connectionId) return
    const scope = { connectionKey, connectionId }
    if (!isCurrentMcpManagementScope(scope)) return
    const statuses = await listMcpManagementStatuses(scope)
    if (isCurrentMcpManagementScope(scope)) useMcpStore.getState().setStatuses(statuses as McpServerStatusWire[])
  }, [enabled, status, connectionKey, connectionId])

  useEffect(() => {
    setMcpServers([])
    useMcpStore.getState().setStatuses([])
  }, [enabled, status, connectionKey, connectionId])

  useEffect(() => {
    setMcpError(null)
    if (!enabled || status !== 'connected' || !connectionKey || !connectionId) {
      setMcpLoading(false)
      return
    }
    const scope = { connectionKey, connectionId }
    let cancelled = false
    setMcpLoading(true)
    void Promise.all([reloadMcpServers(), reloadMcpStatuses()])
      .catch((error: unknown) => {
        if (!cancelled && isCurrentMcpManagementScope(scope)) {
          setMcpServers([])
          setMcpError(error instanceof Error ? error.message : String(error))
        }
      })
      .finally(() => {
        if (!cancelled && isCurrentMcpManagementScope(scope)) setMcpLoading(false)
      })
    return () => { cancelled = true }
  }, [enabled, status, connectionKey, connectionId, catalogRevision, reloadMcpServers, reloadMcpStatuses])

  return { mcpStatuses, mcpServers, mcpLoading, mcpError, reloadMcpServers, reloadMcpStatuses }
}

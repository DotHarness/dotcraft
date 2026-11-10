import { useCallback, useEffect, useState } from 'react'
import { callDesktopPluginMcpTool } from '../../../plugins/desktopPluginMcp'
import { useConnectionStore } from '../../../stores/connectionStore'
import { useMcpStore } from '../../../stores/mcpStore'
import { isCurrentMcpManagementScope, listMcpManagementStatuses } from '../../../stores/mcpManagement'
import {
  parseSettingsDefinition,
  settingsToolsForServer,
  type McpSettingsDefinition,
  type McpSettingsServerStatus,
  type McpSettingsTools,
  type McpSettingsValue
} from './mcpSettingsModel'

export interface PluginSettingsServer {
  status: McpSettingsServerStatus
  tools: McpSettingsTools
}

export function usePluginSettingsServers(pluginId: string, active: boolean): PluginSettingsServer[] {
  const [servers, setServers] = useState<PluginSettingsServer[]>([])
  const { status, connectionKey, connectionId } = useConnectionStore()
  const catalogRevision = useMcpStore((state) => state.catalogRevision)

  useEffect(() => {
    if (!active || status !== 'connected' || !connectionKey || !connectionId) {
      setServers([])
      return
    }
    const scope = { connectionKey, connectionId }
    let cancelled = false
    setServers([])
    void listMcpManagementStatuses(scope)
      .then((statuses) => {
        if (cancelled || !isCurrentMcpManagementScope(scope)) return
        setServers((statuses as McpSettingsServerStatus[])
          .filter((status) => status.origin?.kind === 'plugin' && status.origin.pluginId === pluginId)
          .flatMap((status) => {
            const tools = settingsToolsForServer(status)
            return tools ? [{ status, tools }] : []
          }))
      })
      .catch(() => {
        if (!cancelled && isCurrentMcpManagementScope(scope)) setServers([])
      })
    return () => { cancelled = true }
  }, [active, status, connectionKey, connectionId, pluginId, catalogRevision])

  return servers
}

export interface McpServerSettingsState {
  definition: McpSettingsDefinition | null
  loading: boolean
  failed: boolean
  pendingChange: { name: string; value: McpSettingsValue } | null
  update: (name: string, value: McpSettingsValue) => void
  retry: () => void
}

export function useMcpServerSettings({ status, tools }: PluginSettingsServer): McpServerSettingsState {
  const [definition, setDefinition] = useState<McpSettingsDefinition | null>(null)
  const [loading, setLoading] = useState(true)
  const [readFailed, setReadFailed] = useState(false)
  const [saveFailed, setSaveFailed] = useState(false)
  const [pendingChange, setPendingChange] = useState<McpServerSettingsState['pendingChange']>(null)

  const read = useCallback(async (): Promise<void> => {
    setLoading(true)
    try {
      const result = await callDesktopPluginMcpTool({ server: status.name, tool: tools.readTool, arguments: {} })
      const parsed = result.isError ? null : parseSettingsDefinition(result.structuredContent)
      if (parsed) setDefinition(parsed)
      setReadFailed(parsed == null)
    } catch {
      setReadFailed(true)
    } finally {
      setLoading(false)
    }
  }, [status.name, tools.readTool])

  useEffect(() => { void read() }, [read])

  const update = useCallback((name: string, value: McpSettingsValue) => {
    setPendingChange({ name, value })
    setSaveFailed(false)
    void callDesktopPluginMcpTool({
      server: status.name,
      tool: tools.updateTool,
      arguments: { set: { [name]: value } }
    })
      .then((result) => {
        if (result.isError) setSaveFailed(true)
      })
      .catch(() => setSaveFailed(true))
      .finally(async () => {
        await read()
        setPendingChange(null)
      })
  }, [read, status.name, tools.updateTool])

  const retry = useCallback(() => {
    setSaveFailed(false)
    void read()
  }, [read])

  return { definition, loading, failed: readFailed || saveFailed, pendingChange, update, retry }
}

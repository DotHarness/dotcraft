import { useCallback, useEffect, useState } from 'react'
import { callDesktopPluginMcpTool } from '../../../plugins/desktopPluginMcp'
import { useConnectionStore } from '../../../stores/connectionStore'
import { useMcpStore } from '../../../stores/mcpStore'
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

async function listServerStatuses(): Promise<McpSettingsServerStatus[]> {
  const statuses: McpSettingsServerStatus[] = []
  let cursor: string | null | undefined
  do {
    const page = await window.api.appServer.sendRequestRaw('mcpServerStatus/list', {
      detail: 'toolsAndAuthOnly',
      ...(cursor ? { cursor } : {})
    }) as { data?: McpSettingsServerStatus[]; nextCursor?: string | null }
    statuses.push(...(page.data ?? []))
    cursor = page.nextCursor
  } while (cursor)
  return statuses
}

// Capabilities exist only after initialization, so the list follows the servers' startup state.
// Startup notifications carry no origin, so every server's state is observed.
export function usePluginSettingsServers(pluginId: string, active: boolean): PluginSettingsServer[] {
  const [servers, setServers] = useState<PluginSettingsServer[]>([])
  const connectionEpoch = useConnectionStore((state) => state.connectionEpoch)
  const startupKey = useMcpStore((state) => Object.values(state.statuses)
    .map((status) => `${status.name}:${status.startupState}`)
    .sort()
    .join('|'))

  useEffect(() => {
    if (!active) {
      setServers([])
      return
    }
    let cancelled = false
    void listServerStatuses()
      .then((statuses) => {
        if (cancelled) return
        setServers(statuses
          .filter((status) => status.origin?.kind === 'plugin' && status.origin.pluginId === pluginId)
          .flatMap((status) => {
            const tools = settingsToolsForServer(status)
            return tools ? [{ status, tools }] : []
          }))
      })
      .catch(() => {
        if (!cancelled) setServers([])
      })
    return () => { cancelled = true }
  }, [active, connectionEpoch, pluginId, startupKey])

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

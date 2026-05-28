import { create } from 'zustand'

export type McpTransport = 'stdio' | 'streamableHttp'
export type McpStartupState = 'idle' | 'starting' | 'ready' | 'error' | 'disabled'

export interface McpServerOriginWire {
  kind: 'workspace' | 'plugin'
  pluginId?: string | null
  pluginDisplayName?: string | null
  declaredName?: string | null
}

export interface McpServerConfigWire {
  name: string
  enabled: boolean
  transport: McpTransport
  command?: string | null
  args?: string[] | null
  env?: Record<string, string> | null
  envVars?: string[] | null
  cwd?: string | null
  url?: string | null
  bearerTokenEnvVar?: string | null
  httpHeaders?: Record<string, string> | null
  envHttpHeaders?: Record<string, string> | null
  startupTimeoutSec?: number | null
  toolTimeoutSec?: number | null
  origin?: McpServerOriginWire | null
  readOnly?: boolean
}

export interface McpServerStatusWire {
  name: string
  enabled: boolean
  startupState: McpStartupState
  toolCount?: number | null
  resourceCount?: number | null
  resourceTemplateCount?: number | null
  lastError?: string | null
  transport: McpTransport
  origin?: McpServerOriginWire | null
  readOnly?: boolean
}

interface McpStore {
  statuses: Record<string, McpServerStatusWire>
  setStatuses(statuses: McpServerStatusWire[]): void
  upsertStatus(status: McpServerStatusWire): void
  reset(): void
}

function normalizeKey(name: string): string {
  return name.trim().toLowerCase()
}

export const useMcpStore = create<McpStore>((set) => ({
  statuses: {},

  setStatuses(statuses) {
    const next: Record<string, McpServerStatusWire> = {}
    for (const status of statuses) {
      next[normalizeKey(status.name)] = status
    }
    set({ statuses: next })
  },

  upsertStatus(status) {
    set((state) => ({
      statuses: {
        ...state.statuses,
        [normalizeKey(status.name)]: status
      }
    }))
  },

  reset() {
    set({ statuses: {} })
  }
}))

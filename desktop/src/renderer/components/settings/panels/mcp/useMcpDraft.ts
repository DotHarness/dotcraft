import { useState } from 'react'
import type { McpServerConfigWire, McpOAuthConfigWire } from '../../../../stores/mcpStore'
import {
  normalizeKeyValueRows,
  normalizeValueRows,
  rowsToRecord,
  rowsToValues,
  type KeyValueRow,
  type ValueRow
} from '../../ui/EditableList'

function createEmptyMcpServer(): McpServerConfigWire {
  return {
    name: '',
    enabled: true,
    transport: 'stdio',
    command: '',
    args: [],
    env: {},
    envVars: [],
    cwd: '',
    url: '',
    bearerTokenEnvVar: '',
    httpHeaders: {},
    envHttpHeaders: {},
    startupTimeoutSec: null,
    toolTimeoutSec: null
  }
}

/** The AppServer's read projection of a saved literal secret; never written back. */
export const MASKED_CLIENT_SECRET = '********'

export function oauthPayload(
  value: McpOAuthConfigWire | null | undefined
): McpOAuthConfigWire | null {
  if (!value) return null
  const { hasClientSecret: _hasClientSecret, ...oauth } = value
  if (oauth.clientSecret === MASKED_CLIENT_SECRET) delete oauth.clientSecret
  return oauth
}

/** Mirrors the AppServer rule that OAuth cannot be combined with explicit Authorization. */
export function hasExplicitAuthorization(
  draft: McpServerConfigWire,
  headerRows: KeyValueRow[],
  envHeaderRows: KeyValueRow[]
): boolean {
  if (draft.bearerTokenEnvVar?.trim()) return true
  return [...Object.keys(rowsToRecord(headerRows)), ...Object.keys(rowsToRecord(envHeaderRows))].some(
    (key) => key.toLowerCase() === 'authorization'
  )
}

export function useMcpDraft() {
  const [editingServerName, setEditingServerName] = useState<string | null>(null)
  const [mcpDraft, setMcpDraft] = useState<McpServerConfigWire>(createEmptyMcpServer())
  const [argRows, setArgRows] = useState<ValueRow[]>(normalizeValueRows([]))
  const [envRows, setEnvRows] = useState<KeyValueRow[]>(normalizeKeyValueRows({}))
  const [envVarRows, setEnvVarRows] = useState<ValueRow[]>(normalizeValueRows([]))
  const [httpHeaderRows, setHttpHeaderRows] = useState<KeyValueRow[]>(normalizeKeyValueRows({}))
  const [envHttpHeaderRows, setEnvHttpHeaderRows] = useState<KeyValueRow[]>(
    normalizeKeyValueRows({})
  )
  function startMcpDraft(server?: McpServerConfigWire): void {
    const next = server
      ? {
          ...createEmptyMcpServer(),
          ...server,
          oauth: server.oauth ? { ...server.oauth } : null,
          args: [...(server.args ?? [])],
          env: { ...(server.env ?? {}) },
          envVars: [...(server.envVars ?? [])],
          httpHeaders: { ...(server.httpHeaders ?? {}) },
          envHttpHeaders: { ...(server.envHttpHeaders ?? {}) }
        }
      : createEmptyMcpServer()
    setEditingServerName(server?.name ?? '__new__')
    setMcpDraft(next)
    setArgRows(normalizeValueRows(next.args))
    setEnvRows(normalizeKeyValueRows(next.env))
    setEnvVarRows(normalizeValueRows(next.envVars))
    setHttpHeaderRows(normalizeKeyValueRows(next.httpHeaders))
    setEnvHttpHeaderRows(normalizeKeyValueRows(next.envHttpHeaders))
  }

  function cancelMcpEdit(): void {
    setEditingServerName(null)
    setMcpDraft(createEmptyMcpServer())
    setArgRows(normalizeValueRows([]))
    setEnvRows(normalizeKeyValueRows({}))
    setEnvVarRows(normalizeValueRows([]))
    setHttpHeaderRows(normalizeKeyValueRows({}))
    setEnvHttpHeaderRows(normalizeKeyValueRows({}))
  }

  function buildDraftPayload(): McpServerConfigWire {
    const transport = mcpDraft.transport
    return {
      name: mcpDraft.name.trim(),
      enabled: mcpDraft.enabled,
      transport,
      command: transport === 'stdio' ? (mcpDraft.command?.trim() ?? '') : null,
      args: transport === 'stdio' ? rowsToValues(argRows) : null,
      env: transport === 'stdio' ? rowsToRecord(envRows) : null,
      envVars: transport === 'stdio' ? rowsToValues(envVarRows) : null,
      cwd: transport === 'stdio' ? mcpDraft.cwd?.trim() || null : null,
      url: transport === 'streamableHttp' ? (mcpDraft.url?.trim() ?? '') : null,
      bearerTokenEnvVar:
        transport === 'streamableHttp' ? mcpDraft.bearerTokenEnvVar?.trim() || null : null,
      httpHeaders: transport === 'streamableHttp' ? rowsToRecord(httpHeaderRows) : null,
      envHttpHeaders: transport === 'streamableHttp' ? rowsToRecord(envHttpHeaderRows) : null,
      startupTimeoutSec: mcpDraft.startupTimeoutSec ?? null,
      toolTimeoutSec: mcpDraft.toolTimeoutSec ?? null,
      oauth: transport === 'streamableHttp' ? oauthPayload(mcpDraft.oauth) : null
    }
  }

  return {
    editingServerName,
    setEditingServerName,
    mcpDraft,
    setMcpDraft,
    argRows,
    setArgRows,
    envRows,
    setEnvRows,
    envVarRows,
    setEnvVarRows,
    httpHeaderRows,
    setHttpHeaderRows,
    envHttpHeaderRows,
    setEnvHttpHeaderRows,
    startMcpDraft,
    cancelMcpEdit,
    buildDraftPayload
  }
}

/**
 * Headers for the RemoteToolHost.List / Connect / Disconnect rows. The timeline divider
 * already announced the move, so a row says what the call did, not where the thread now runs.
 */

import { translate, type AppLocale } from '../../shared/locales'
import type { ConversationItem } from '../types/conversation'
import { basename } from './path'

export type RemoteToolHostOperation = 'list' | 'connect' | 'disconnect'

export type RemoteToolHostPhase = 'running' | 'completed' | 'failed'

export interface RemoteToolHostWorkspaceEntry {
  workspaceId: string
  displayName: string
  available: boolean
  busyOwner?: string
}

export interface RemoteToolHostEntry {
  hostId: string
  displayName: string
  online: boolean
  workspaces: RemoteToolHostWorkspaceEntry[]
}

export interface RemoteToolHostCatalog {
  hosts: RemoteToolHostEntry[]
  connectedRoute?: { hostId: string; workspaceId: string }
}

export interface RemoteToolHostNames {
  /** No id while a Disconnect runs, so the caller answers with the thread's own route. */
  host(hostId?: string): string | undefined
  workspace(workspaceId?: string): string | undefined
}

export type RemoteToolHostToolItem = Pick<
  ConversationItem,
  'presentation' | 'arguments' | 'result' | 'errorCode' | 'errorMessage'
>

const KEY = 'toolCall.remoteToolHost'

export function readRemoteToolHostOperation(
  item: Pick<ConversationItem, 'presentation'>
): RemoteToolHostOperation | null {
  const operation = item.presentation?.options?.operation
  return operation === 'list' || operation === 'connect' || operation === 'disconnect'
    ? operation
    : null
}

/**
 * The row's collapsed header. Null means the result did not carry what the
 * sentence needs, and the caller should keep its generic label.
 */
export function formatRemoteToolHostLabel(
  item: RemoteToolHostToolItem,
  phase: RemoteToolHostPhase,
  locale: AppLocale,
  names: RemoteToolHostNames
): string | null {
  const operation = readRemoteToolHostOperation(item)
  if (operation === 'list') return listLabel(item, phase, locale)
  if (operation === 'connect') return connectLabel(item, phase, locale, names)
  if (operation === 'disconnect') return disconnectLabel(item, phase, locale, names)
  return null
}

export function parseRemoteToolHostCatalog(result: string | undefined): RemoteToolHostCatalog | null {
  const root = parseObject(result)
  const hosts = Array.isArray(root?.hosts) ? root.hosts : null
  if (!hosts) return null

  const route = asRecord(root?.connectedRoute)
  const hostId = text(route?.hostId)
  const workspaceId = text(route?.workspaceId)
  return {
    hosts: hosts.flatMap((entry) => hostEntry(entry) ?? []),
    ...(hostId && workspaceId ? { connectedRoute: { hostId, workspaceId } } : {})
  }
}

function listLabel(
  item: RemoteToolHostToolItem,
  phase: RemoteToolHostPhase,
  locale: AppLocale
): string | null {
  if (phase === 'running') return translate(locale, `${KEY}.list.running`)
  if (phase === 'failed') return translate(locale, `${KEY}.list.failed`)
  const catalog = parseRemoteToolHostCatalog(item.result)
  if (!catalog) return null
  const online = catalog.hosts.filter((host) => host.online).length
  return translate(locale, `${KEY}.list.completed`, { online })
}

function connectLabel(
  item: RemoteToolHostToolItem,
  phase: RemoteToolHostPhase,
  locale: AppLocale,
  names: RemoteToolHostNames
): string | null {
  const requestedHostId = text(item.arguments?.hostId)

  if (phase !== 'completed') {
    const host = names.host(requestedHostId) ?? requestedHostId ?? unknownHost(locale)
    if (phase === 'running') return translate(locale, `${KEY}.connect.running`, { host })
    const failure = translate(locale, `${KEY}.connect.failed`, { host })
    const reason = failureReason(item, locale)
    return reason ? `${failure} · ${reason}` : failure
  }

  const root = parseObject(item.result)
  if (!root) return null
  const environment = asRecord(root.environment)
  const route = asRecord(root.route)
  const workspacePath = text(environment?.workspacePath)
  const workspaceId = text(route?.workspaceId) ?? text(item.arguments?.workspaceId)
  const folder = (workspacePath ? basename(workspacePath) : undefined)
    ?? names.workspace(workspaceId)
    ?? workspaceId
  if (!folder) return null

  const hostId = text(route?.hostId) ?? requestedHostId
  const host = text(environment?.hostName) ?? names.host(hostId) ?? hostId ?? unknownHost(locale)
  const count = Array.isArray(root.matchedTools) ? root.matchedTools.length : 0
  const tools = translate(locale, `${KEY}.connect.tools.${count === 1 ? 'one' : 'other'}`, { count })
  return `${translate(locale, `${KEY}.connect.completed`, { folder, host })} · ${tools}`
}

function disconnectLabel(
  item: RemoteToolHostToolItem,
  phase: RemoteToolHostPhase,
  locale: AppLocale,
  names: RemoteToolHostNames
): string | null {
  if (phase !== 'completed') {
    const host = names.host() ?? unknownHost(locale)
    return translate(locale, `${KEY}.disconnect.${phase === 'running' ? 'running' : 'failed'}`, { host })
  }

  const root = parseObject(item.result)
  if (!root || typeof root.disconnected !== 'boolean') return null
  if (!root.disconnected) return translate(locale, `${KEY}.disconnect.none`)

  const previousHostId = text(asRecord(root.previousRoute)?.hostId)
  const host = names.host(previousHostId) ?? previousHostId ?? unknownHost(locale)
  return translate(locale, `${KEY}.disconnect.completed`, { host })
}

/** A stable `RemoteToolErrorCodes` value first; the server's own sentence otherwise. */
function failureReason(item: RemoteToolHostToolItem, locale: AppLocale): string | undefined {
  const code = text(item.errorCode)
  if (code) {
    const key = `error.remoteToolHost.${camelCase(code)}`
    const localized = translate(locale, key)
    if (localized !== key) return localized
  }
  return text(item.errorMessage)
}

function camelCase(code: string): string {
  return code.trim().replace(/_([a-z0-9])/g, (_, character: string) => character.toUpperCase())
}

function unknownHost(locale: AppLocale): string {
  return translate(locale, `${KEY}.unknownHost`)
}

function hostEntry(value: unknown): RemoteToolHostEntry | null {
  const record = asRecord(value)
  const hostId = text(record?.hostId)
  if (!record || !hostId) return null
  const workspaces = Array.isArray(record.workspaces) ? record.workspaces : []
  return {
    hostId,
    displayName: text(record.displayName) ?? hostId,
    online: record.online === true,
    workspaces: workspaces.flatMap((entry) => workspaceEntry(entry) ?? [])
  }
}

function workspaceEntry(value: unknown): RemoteToolHostWorkspaceEntry | null {
  const record = asRecord(value)
  const workspaceId = text(record?.workspaceId)
  if (!record || !workspaceId) return null
  const busyOwner = text(record.busyOwner)
  return {
    workspaceId,
    displayName: text(record.displayName) ?? workspaceId,
    available: record.available !== false,
    ...(busyOwner ? { busyOwner } : {})
  }
}

function parseObject(result: string | undefined): Record<string, unknown> | null {
  const trimmed = result?.trim() ?? ''
  if (trimmed === '') return null
  try {
    return asRecord(JSON.parse(trimmed))
  } catch {
    return null
  }
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value != null && typeof value === 'object' && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : null
}

function text(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim() !== '' ? value : undefined
}

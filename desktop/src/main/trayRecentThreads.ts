import { basename } from 'path'
import type { HubAppServerResponse } from './desktopHub'
import { DesktopAppServerClient } from './DesktopAppServerClient'

export const DIRECT_RECENT_THREAD_COUNT = 3
export const MAX_RECENT_THREAD_COUNT = 12
export const RECENT_THREAD_TITLE_MAX_LENGTH = 35

const CONNECTION_READY_TIMEOUT_MS = 3_000
const THREAD_LIST_TIMEOUT_MS = 5_000
const THREAD_LIST_LIMIT = MAX_RECENT_THREAD_COUNT

export interface TrayRecentThread {
  id: string
  displayName: string | null
  lastActiveAt: string
  workspacePath: string
  workspaceName: string
}

interface ThreadListResult {
  data?: unknown[]
}

interface TrayThreadClient {
  sendRequest<T = unknown>(method: string, params?: unknown, timeoutMs?: number | null): Promise<T>
  dispose(): void
  on(event: string, listener: (...args: unknown[]) => void): this
  off(event: string, listener: (...args: unknown[]) => void): this
}

interface TrayThreadConnection {
  client: TrayThreadClient
  ready: boolean
  threads: TrayRecentThread[]
  workspaceName: string
  workspacePath: string
}

export type TrayThreadClientFactory = (endpoint: string) => TrayThreadClient

function createTrayThreadClient(endpoint: string): TrayThreadClient {
  return DesktopAppServerClient.fromWebSocket(endpoint, {
    autoReconnect: true,
    initializeProfile: 'secondary',
    initializeTimeoutMs: CONNECTION_READY_TIMEOUT_MS
  })
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

function stringField(value: unknown): string {
  return typeof value === 'string' ? value.trim() : ''
}

function isSubAgentThread(thread: Record<string, unknown>): boolean {
  const source = isRecord(thread.source) ? thread.source : null
  return stringField(source?.kind).toLowerCase() === 'subagent' ||
    stringField(thread.originChannel).toLowerCase() === 'subagent'
}

function parseRecentThread(
  value: unknown,
  workspacePath: string,
  workspaceName: string
): TrayRecentThread | null {
  if (!isRecord(value)) return null
  if (stringField(value.status).toLowerCase() === 'archived' || isSubAgentThread(value)) return null

  const id = stringField(value.id)
  const lastActiveAt = stringField(value.lastActiveAt)
  const timestamp = Date.parse(lastActiveAt)
  if (!id || !Number.isFinite(timestamp)) return null

  return {
    id,
    displayName: stringField(value.displayName) || null,
    lastActiveAt,
    workspacePath: stringField(value.workspacePath) || workspacePath,
    workspaceName
  }
}

function appServerEndpoint(server: HubAppServerResponse): string {
  const state = server.state.trim().toLowerCase()
  if (state !== 'running' && state !== 'healthy' && state !== 'ready') return ''
  return stringField(
    server.endpoints.appServerWebSocket ?? server.serviceStatus.appServerWebSocket?.url
  )
}

function workspacePathOf(server: HubAppServerResponse): string {
  return stringField(server.canonicalWorkspacePath) || stringField(server.workspacePath)
}

function recentThreadIdentity(workspacePath: string): Record<string, unknown> {
  return {
    channelName: 'dotcraft-desktop',
    userId: 'local',
    channelContext: `workspace:${workspacePath}`,
    workspacePath
  }
}

function sortRecentThreads(threads: TrayRecentThread[]): TrayRecentThread[] {
  return threads
    .sort((left, right) => Date.parse(right.lastActiveAt) - Date.parse(left.lastActiveAt))
    .slice(0, MAX_RECENT_THREAD_COUNT)
}

export class TrayRecentThreadCatalog {
  private readonly connections = new Map<string, TrayThreadConnection>()
  private disposed = false

  constructor(private readonly createClient: TrayThreadClientFactory = createTrayThreadClient) {}

  async refresh(appServers: HubAppServerResponse[]): Promise<TrayRecentThread[]> {
    if (this.disposed) return []

    const targets = new Map<string, { endpoint: string; workspacePath: string; workspaceName: string }>()
    for (const server of appServers) {
      const endpoint = appServerEndpoint(server)
      const workspacePath = workspacePathOf(server)
      if (!endpoint || !workspacePath) continue
      targets.set(endpoint, {
        endpoint,
        workspacePath,
        workspaceName: basename(workspacePath) || workspacePath
      })
    }

    for (const [endpoint, connection] of this.connections) {
      if (targets.has(endpoint)) continue
      connection.client.dispose()
      this.connections.delete(endpoint)
    }

    for (const target of targets.values()) {
      if (this.connections.has(target.endpoint)) continue
      this.connections.set(target.endpoint, this.createConnection(target))
    }

    await Promise.allSettled(
      [...this.connections.values()].map(async (connection) => {
        if (!await this.waitUntilReady(connection)) return
        await this.refreshConnection(connection)
      })
    )

    return sortRecentThreads(
      [...this.connections.values()].flatMap((connection) => connection.threads)
    )
  }

  dispose(): void {
    if (this.disposed) return
    this.disposed = true
    for (const connection of this.connections.values()) {
      connection.client.dispose()
    }
    this.connections.clear()
  }

  private createConnection(target: {
    endpoint: string
    workspacePath: string
    workspaceName: string
  }): TrayThreadConnection {
    const client = this.createClient(target.endpoint)
    const connection: TrayThreadConnection = {
      client,
      ready: false,
      threads: [],
      workspaceName: target.workspaceName,
      workspacePath: target.workspacePath
    }
    client.on('ready', () => {
      connection.ready = true
    })
    client.on('reconnected', () => {
      connection.ready = true
    })
    client.on('close', () => {
      connection.ready = false
    })
    client.on('reconnect-error', () => {
      connection.ready = false
    })
    return connection
  }

  private async waitUntilReady(connection: TrayThreadConnection): Promise<boolean> {
    if (connection.ready) return true

    return await new Promise<boolean>((resolve) => {
      let settled = false
      const finish = (ready: boolean): void => {
        if (settled) return
        settled = true
        clearTimeout(timer)
        connection.client.off('ready', onReady)
        connection.client.off('reconnected', onReady)
        connection.client.off('reconnect-error', onUnavailable)
        resolve(ready)
      }
      const onReady = (): void => finish(true)
      const onUnavailable = (): void => finish(false)
      const timer = setTimeout(() => finish(connection.ready), CONNECTION_READY_TIMEOUT_MS)
      timer.unref?.()
      connection.client.on('ready', onReady)
      connection.client.on('reconnected', onReady)
      connection.client.on('reconnect-error', onUnavailable)
      if (connection.ready) finish(true)
    })
  }

  private async refreshConnection(connection: TrayThreadConnection): Promise<void> {
    const result = await connection.client.sendRequest<ThreadListResult>('thread/list', {
      identity: recentThreadIdentity(connection.workspacePath),
      scope: 'workspace',
      includeArchived: false,
      includeSubAgents: false,
      includeInternal: false,
      limit: THREAD_LIST_LIMIT
    }, THREAD_LIST_TIMEOUT_MS)
    const data = Array.isArray(result.data) ? result.data : []
    connection.threads = data
      .map((thread) => parseRecentThread(thread, connection.workspacePath, connection.workspaceName))
      .filter((thread): thread is TrayRecentThread => thread != null)
  }
}

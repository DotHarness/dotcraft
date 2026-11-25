import { BrowserWindow, powerMonitor } from 'electron'
import type { HubAppServerResponse } from './desktopHub'
import { DesktopAppServerClient } from './DesktopAppServerClient'
import { appServerEndpoint, recentThreadIdentity, workspacePathOf } from './trayRecentThreads'

const AWAY_IDLE_SECONDS = 10 * 60
const READY_TIMEOUT_MS = 5_000
const THREAD_LIST_TIMEOUT_MS = 15_000

export interface AppUpdateIdleClient {
  sendRequest<T = unknown>(method: string, params?: unknown, timeoutMs?: number | null): Promise<T>
  once(event: string, listener: (...args: unknown[]) => void): this
  dispose(): void
}

export interface AppUpdateIdleProbeOptions {
  listAppServers(): Promise<HubAppServerResponse[]>
  isUserAway?: () => boolean
  createClient?: (endpoint: string) => AppUpdateIdleClient
}

export function isUserAwayFromDesktop(): boolean {
  return BrowserWindow.getFocusedWindow() === null || powerMonitor.getSystemIdleTime() >= AWAY_IDLE_SECONDS
}

export function isThreadBusyForUpdate(thread: unknown): boolean {
  if (!isRecord(thread) || !isRecord(thread.runtime)) return false
  const runtime = thread.runtime
  return Boolean(runtime.activeTurnId) || runtime.running === true || runtime.busy === true ||
    runtime.hasBackgroundWork === true || runtime.waitingOnApproval === true ||
    runtime.waitingOnInput === true || runtime.waitingOnPlanConfirmation === true
}

export async function isIdleForAutomaticUpdate(options: AppUpdateIdleProbeOptions): Promise<boolean> {
  if (!(options.isUserAway ?? isUserAwayFromDesktop)()) return false
  const servers = await options.listAppServers()
  const targets = servers
    .map((server) => ({ endpoint: appServerEndpoint(server), workspacePath: workspacePathOf(server) }))
    .filter((target) => target.endpoint && target.workspacePath)
  const createClient = options.createClient ?? createIdleClient
  const results = await Promise.all(targets.map(async ({ endpoint, workspacePath }) => {
    const client = createClient(endpoint)
    try {
      if (!await waitUntilReady(client)) return false
      const result = await client.sendRequest<{ data?: unknown[] }>('thread/list', {
        identity: recentThreadIdentity(workspacePath),
        scope: 'workspace',
        includeArchived: false,
        includeSubAgents: false,
        includeInternal: true
      }, THREAD_LIST_TIMEOUT_MS)
      return !(result.data ?? []).some(isThreadBusyForUpdate)
    } catch {
      return false
    } finally {
      client.dispose()
    }
  }))
  return results.every(Boolean)
}

function createIdleClient(endpoint: string): AppUpdateIdleClient {
  return DesktopAppServerClient.fromWebSocket(endpoint, {
    autoReconnect: false,
    initializeTimeoutMs: READY_TIMEOUT_MS
  })
}

function waitUntilReady(client: AppUpdateIdleClient): Promise<boolean> {
  return new Promise((resolve) => {
    const timer = setTimeout(() => resolve(false), READY_TIMEOUT_MS)
    timer.unref()
    client.once('ready', () => {
      clearTimeout(timer)
      resolve(true)
    })
    client.once('reconnect-error', () => {
      clearTimeout(timer)
      resolve(false)
    })
  })
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

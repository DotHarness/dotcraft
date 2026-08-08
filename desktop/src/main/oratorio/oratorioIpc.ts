import { BrowserWindow, ipcMain } from 'electron'

import type { OratorioHandoffRequest, OratorioRequest, OratorioServiceEvent } from '../../shared/oratorio'
import type { DesktopHubClient } from '../desktopHub'
import { setDesktopServiceHandoffHandler } from '../desktopServiceHandoff'
import { OratorioProvider, type RemoteOratorioService } from './OratorioProvider'

const HANDLERS = ['oratorio:get-context', 'oratorio:request', 'oratorio:retry', 'oratorio:get-pending-handoff', 'oratorio:resolve-handoff', 'oratorio:focus-run'] as const
const SUBSCRIPTION_CHANNELS = ['oratorio:subscribe', 'oratorio:unsubscribe'] as const
const GET_PATHS = [
  /^\/api\/v1\/tasks(?:\?.*)?$/,
  /^\/api\/v1\/tasks\/[^/?]+$/,
  /^\/api\/v1\/sources\/sync-schedules$/,
  /^\/api\/v1\/settings\/server-configuration$/
]
const POST_PATHS = [
  /^\/api\/v1\/local-tasks$/,
  /^\/api\/v1\/tasks\/reorder$/,
  /^\/api\/v1\/items\/id\/[^/?]+\/comments$/,
  /^\/api\/v1\/items\/id\/[^/?]+\/(?:discussion-turns|source-details\/sync)$/,
  /^\/api\/v1\/sources\/(?:github|gitlab)\/sync-jobs$/,
  /^\/api\/v1\/items\/id\/[^/?]+\/(?:dispatch|cancel-run|rereview|approve|request-changes|reject|reopen|archive)$/,
  /^\/api\/v1\/source-writes\/[^/?]+\/retry$/,
  /^\/api\/v1\/review-drafts\/[^/?]+\/(?:publish|discard)$/,
  /^\/api\/v1\/review-drafts\/[^/?]+\/comments\/[^/?]+\/(?:resolve|reopen)$/,
  /^\/api\/v1\/implementation-drafts\/[^/?]+\/deliver$/,
  /^\/api\/v1\/follow-up-drafts\/[^/?]+\/(?:discard|create-local-task)$/
]
const PUT_PATHS = [
  /^\/api\/v1\/settings\/server-configuration$/,
  /^\/api\/v1\/sources\/(?:github|gitlab)\/sync-schedule$/
]
const PATCH_PATHS = [
  /^\/api\/v1\/review-drafts\/[^/?]+$/,
  /^\/api\/v1\/follow-up-drafts\/[^/?]+$/
]

let provider: OratorioProvider | null = null
let revision = 1
const subscribedSenders = new Set<number>()

export function registerOratorioIpc(
  getWorkspacePath: () => string | null,
  getHubClient?: () => DesktopHubClient,
  resolveExecutable?: () => string,
  resolveRemoteService?: () => Promise<RemoteOratorioService | null>
): void {
  for (const channel of HANDLERS) ipcMain.removeHandler(channel)
  for (const channel of SUBSCRIPTION_CHANNELS) ipcMain.removeAllListeners(channel)
  subscribedSenders.clear()
  provider?.disconnect()
  provider = getHubClient
    ? new OratorioProvider(getHubClient, getWorkspacePath, resolveExecutable, (nextRevision, event) => {
        revision = nextRevision
        broadcast(event ? { type: 'board-event', revision, event } : { type: 'data-changed', revision })
      }, resolveRemoteService)
    : null
  setDesktopServiceHandoffHandler(provider ? async (url) => {
    const handoff = await requireProvider().prepareHandoff(url)
    broadcastHandoff(handoff)
  } : null)

  ipcMain.handle('oratorio:get-context', () => requireProvider().getContext())
  ipcMain.handle('oratorio:request', (_event, value: unknown) => requireProvider().request(validateRequest(value)))
  ipcMain.handle('oratorio:retry', async () => {
    const context = await requireProvider().retry()
    revision = context.revision
    broadcast({ type: 'context-changed', revision })
    return { ...context, revision }
  })
  ipcMain.handle('oratorio:get-pending-handoff', () => requireProvider().getPendingHandoff())
  ipcMain.handle('oratorio:resolve-handoff', (_event, value: unknown) => {
    if (!value || typeof value !== 'object') throw new Error('oratorio.invalid_handoff_resolution')
    const resolution = value as { requestId?: unknown; approved?: unknown }
    if (typeof resolution.requestId !== 'string' || typeof resolution.approved !== 'boolean') throw new Error('oratorio.invalid_handoff_resolution')
    return requireProvider().resolveHandoff(resolution.requestId, resolution.approved)
  })
  ipcMain.handle('oratorio:focus-run', (_event, runId: unknown) => {
    if (runId !== null && typeof runId !== 'string') throw new Error('oratorio.invalid_run_focus')
    requireProvider().focusRun(runId as string | null)
  })
  ipcMain.on('oratorio:subscribe', (event) => {
    const id = event.sender.id
    if (subscribedSenders.has(id)) return
    subscribedSenders.add(id)
    requireProvider().subscribe()
    event.sender.once('destroyed', () => removeSubscriber(id))
  })
  ipcMain.on('oratorio:unsubscribe', (event) => removeSubscriber(event.sender.id))
}

export function notifyOratorioContextChanged(): void {
  revision = provider?.contextChanged() ?? revision + 1
  broadcast({ type: 'context-changed', revision })
}

export function validateRequest(value: unknown): OratorioRequest {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('oratorio.invalid_request')
  const request = value as Record<string, unknown>
  if (request.method !== 'GET' && request.method !== 'POST' && request.method !== 'PUT' && request.method !== 'PATCH') throw new Error('oratorio.invalid_method')
  if (typeof request.path !== 'string' || request.path.includes('://')) throw new Error('oratorio.invalid_path')
  const allowed = request.method === 'GET' ? GET_PATHS : request.method === 'POST' ? POST_PATHS : request.method === 'PUT' ? PUT_PATHS : PATCH_PATHS
  if (!allowed.some((pattern) => pattern.test(request.path as string))) throw new Error('oratorio.invalid_path')
  if (request.body !== undefined && (!request.body || typeof request.body !== 'object' || Array.isArray(request.body))) {
    throw new Error('oratorio.invalid_body')
  }
  return request as unknown as OratorioRequest
}

function requireProvider(): OratorioProvider {
  if (!provider) throw new Error('oratorio.provider_not_configured')
  return provider
}

function removeSubscriber(id: number): void {
  if (!subscribedSenders.delete(id)) return
  provider?.unsubscribe()
}

function broadcast(event: OratorioServiceEvent): void {
  for (const window of BrowserWindow.getAllWindows()) {
    if (!window.isDestroyed()) window.webContents.send('oratorio:event', event)
  }
}

function broadcastHandoff(handoff: OratorioHandoffRequest): void {
  broadcast({ type: 'handoff-requested', revision, handoff })
}

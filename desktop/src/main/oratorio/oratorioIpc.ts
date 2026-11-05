import type { WebContents } from 'electron'
import type { WindowIpcMain } from '../windowIpc'

import type { OratorioRequest, OratorioServiceEvent } from '../../shared/oratorio'
import type { DesktopHubClient } from '../desktopHub'
import { setDesktopServiceHandoffHandler } from '../desktopServiceHandoff'
import { OratorioProvider, type RemoteOratorioService } from './OratorioProvider'

const GET_PATHS = [
  /^\/api\/v1\/tasks(?:\?.*)?$/,
  /^\/api\/v1\/tasks\/[^/?]+$/,
  /^\/api\/v1\/sources\/sync-schedules$/,
  /^\/api\/v1\/sources\/sync-jobs\/(?!active(?:\?|$))[^/?]+\?provider=(?:github|gitlab)$/,
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
  /^\/api\/v1\/follow-up-drafts\/[^/?]+$/
]

export interface OratorioWindowHandle {
  contextChanged(): void
  dispose(): void
}

export function registerOratorioIpc(
  getWorkspacePath: () => string | null,
  getHubClient: () => DesktopHubClient,
  resolveRemoteService: (() => Promise<RemoteOratorioService | null>) | undefined,
  ipc: WindowIpcMain,
  sender: WebContents
): OratorioWindowHandle {
  let revision = 1
  let subscribed = false
  let disposed = false
  const send = (event: OratorioServiceEvent): void => {
    if (!disposed && !sender.isDestroyed()) sender.send('oratorio:event', event)
  }
  const provider = new OratorioProvider(getHubClient, getWorkspacePath, (nextRevision, event) => {
    revision = nextRevision
    send(event ? { type: 'board-event', revision, event } : { type: 'data-changed', revision })
  }, resolveRemoteService)
  setDesktopServiceHandoffHandler(sender.id, async (url) => {
    const handoff = await provider.handleDesktopServiceHandoff(url)
    if (handoff) send({ type: 'handoff-requested', revision, handoff })
  })

  ipc.handle('oratorio:get-context', () => provider.getContext())
  ipc.handle('oratorio:request', (_event, value: unknown) => provider.request(validateRequest(value)))
  ipc.handle('oratorio:retry', async () => {
    const context = await provider.retry()
    revision = context.revision
    send({ type: 'context-changed', revision })
    return { ...context, revision }
  })
  ipc.handle('oratorio:get-pending-handoff', () => provider.getPendingHandoff())
  ipc.handle('oratorio:resolve-handoff', (_event, value: unknown) => {
    if (!value || typeof value !== 'object') throw new Error('oratorio.invalid_handoff_resolution')
    const resolution = value as { requestId?: unknown; approved?: unknown }
    if (typeof resolution.requestId !== 'string' || typeof resolution.approved !== 'boolean') throw new Error('oratorio.invalid_handoff_resolution')
    return provider.resolveHandoff(resolution.requestId, resolution.approved)
  })
  ipc.handle('oratorio:focus-run', (_event, runId: unknown) => {
    if (runId !== null && typeof runId !== 'string') throw new Error('oratorio.invalid_run_focus')
    provider.focusRun(runId as string | null)
  })
  ipc.on('oratorio:subscribe', () => {
    if (subscribed) return
    subscribed = true
    provider.subscribe()
  })
  ipc.on('oratorio:unsubscribe', () => {
    if (!subscribed) return
    subscribed = false
    provider.unsubscribe()
  })
  const handle: OratorioWindowHandle = {
    contextChanged: () => {
      revision = provider.contextChanged()
      send({ type: 'context-changed', revision })
    },
    dispose: () => {
      if (disposed) return
      disposed = true
      setDesktopServiceHandoffHandler(sender.id, null)
      provider.dispose()
    }
  }
  sender.once('destroyed', handle.dispose)
  return handle
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

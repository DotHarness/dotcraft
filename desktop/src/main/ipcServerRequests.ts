import type { BrowserWindow } from 'electron'
import type { AppServerConnectionSource, RawServerRequestPayload } from '../shared/appServerBoundary'

interface PendingServerRequest {
  ownerId: number
  owner: BrowserWindow
  connectionId?: string
  wireRequestId?: string | number
  detachCancellation?: () => void
  resolve: (result: unknown) => void
  reject: (error: Error) => void
  onSettled?: () => void
}

let nextBridgeId = 1
const pendingServerRequests = new Map<string, PendingServerRequest>()

export function createServerRequestBridge(
  onSettled: (() => void) | undefined,
  owner: BrowserWindow,
  source?: AppServerConnectionSource & { wireRequestId?: string | number },
  signal?: AbortSignal
): {
  bridgeId: string
  promise: Promise<unknown>
} {
  const bridgeId = String(nextBridgeId++)
  const ownerId = owner.webContents.id
  const promise = new Promise<unknown>((resolve, reject) => {
    const pending: PendingServerRequest = {
      ownerId, owner, connectionId: source?.connectionId,
      wireRequestId: source?.wireRequestId, resolve, reject, onSettled
    }
    pendingServerRequests.set(bridgeId, pending)
    if (signal) {
      const cancel = (): void => cancelServerRequestBridge(bridgeId, ownerId)
      pending.detachCancellation = () => signal.removeEventListener('abort', cancel)
      signal.addEventListener('abort', cancel, { once: true })
      if (signal.aborted) cancel()
    }
  })
  return { bridgeId, promise }
}

export function resolveServerRequestBridge(bridgeId: string, result: unknown, ownerId: number): void {
  const pending = takeRequest(bridgeId, ownerId)
  if (pending) pending.resolve(result)
}

export function cancelServerRequestBridge(bridgeId: string, ownerId: number): void {
  const pending = takeRequest(bridgeId, ownerId)
  if (pending) pending.reject(new Error('Interactive request was cancelled with its turn.'))
}

function takeRequest(bridgeId: string, ownerId: number): PendingServerRequest | undefined {
  const pending = pendingServerRequests.get(bridgeId)
  if (!pending) return undefined
  if (pending.ownerId !== ownerId) {
    throw new Error('Interactive request belongs to a different Desktop window.')
  }
  pendingServerRequests.delete(bridgeId)
  pending.detachCancellation?.()
  pending.onSettled?.()
  if (!pending.owner.isDestroyed()) {
    pending.owner.webContents.send('appserver:server-request-settled', {
      bridgeId, connectionId: pending.connectionId, wireRequestId: pending.wireRequestId
    })
  }
  return pending
}

export function cancelWindowServerRequests(owner: BrowserWindow): void {
  for (const [bridgeId, request] of pendingServerRequests) {
    if (request.ownerId === owner.webContents.id) cancelServerRequestBridge(bridgeId, owner.webContents.id)
  }
}

export function cancelConnectionServerRequests(connectionId: string): void {
  for (const [bridgeId, request] of pendingServerRequests) {
    if (request.connectionId === connectionId) cancelServerRequestBridge(bridgeId, request.ownerId)
  }
}

export function broadcastServerRequest(win: BrowserWindow, payload: RawServerRequestPayload): void {
  const pending = pendingServerRequests.get(payload.bridgeId)
  if (!pending) throw new Error('Interactive request is no longer pending.')
  const ownerId = win.webContents.id
  if (pending.ownerId !== ownerId) {
    throw new Error('Interactive request belongs to a different Desktop window.')
  }
  if (win.isDestroyed()) {
    cancelServerRequestBridge(payload.bridgeId, ownerId)
    return
  }
  win.webContents.send('appserver:server-request', payload)
}

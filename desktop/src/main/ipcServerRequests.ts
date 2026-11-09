import type { BrowserWindow } from 'electron'
import type { AppServerConnectionSource } from '../shared/appServerBoundary'

export interface ServerRequestPayload {
  bridgeId: string
  connectionKey?: string
  connectionId?: string
  method: string
  params: unknown
}

interface PendingServerRequest {
  ownerId: number
  connectionId?: string
  resolve: (result: unknown) => void
  reject: (error: Error) => void
  onSettled?: () => void
}

let nextBridgeId = 1
const pendingServerRequests = new Map<string, PendingServerRequest>()

export function createServerRequestBridge(
  onSettled: (() => void) | undefined,
  owner: BrowserWindow,
  source?: AppServerConnectionSource
): {
  bridgeId: string
  promise: Promise<unknown>
} {
  const bridgeId = String(nextBridgeId++)
  const promise = new Promise<unknown>((resolve, reject) => {
    pendingServerRequests.set(bridgeId, { ownerId: owner.webContents.id, connectionId: source?.connectionId, resolve, reject, onSettled })
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
  pending.onSettled?.()
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

export function broadcastServerRequest(win: BrowserWindow, payload: ServerRequestPayload): void {
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

import {
  APP_SERVER_METHOD_GROUPS,
  type ClientRequestMethods,
  type ServerNotificationMethods,
  type ServerRequestMethods
} from '@dotcraft/sdk/contracts'

export type AppServerRequestMethod = keyof ClientRequestMethods

export interface AppServerConnectionSource {
  connectionKey: string
  connectionId: string
}

export interface AppServerResponseOptions<T = unknown> {
  onResponse?: (result: T) => void
  attachmentOwner?: string
  connectionId?: string
}

export interface AppServerIpcRequest {
  requestId: number
  method: string
  params?: unknown
  timeoutMs?: number | null
  connectionKey?: string
  connectionId?: string
  attachmentOwner?: string
}

export type AppServerDelivery =
  | { kind: 'response'; requestId: number; result: unknown }
  | { kind: 'notification'; payload: RawNotificationPayload }

export interface TypedAppServerRequestApi {
  sendRequest<M extends AppServerRequestMethod>(
    method: M,
    params: ClientRequestMethods[M]['params'],
    timeoutMs?: number | null,
    options?: AppServerResponseOptions<ClientRequestMethods[M]['result']>
  ): Promise<ClientRequestMethods[M]['result']>
  sendRequestRaw(method: string, params?: unknown, timeoutMs?: number | null, options?: AppServerResponseOptions): Promise<unknown>
}

export type KnownNotificationPayload = {
  [M in keyof ServerNotificationMethods]: {
    method: M
    params: ServerNotificationMethods[M]['params']
    workspacePath?: string
    foreground?: boolean
    connectionKey?: string
    connectionId?: string
  }
}[keyof ServerNotificationMethods]

export interface RawNotificationPayload {
  method: string
  params: unknown
  workspacePath?: string
  foreground?: boolean
  connectionKey?: string
  connectionId?: string
}

export type KnownServerRequestPayload = {
  [M in keyof ServerRequestMethods]: {
    bridgeId: string
    wireRequestId?: string | number
    connectionKey?: string
    connectionId?: string
    method: M
    params: ServerRequestMethods[M]['params']
  }
}[keyof ServerRequestMethods]

export interface ServerRequestSettledPayload {
  bridgeId: string
  connectionId?: string
  wireRequestId?: string | number
}

export interface RawServerRequestPayload {
  bridgeId: string
  wireRequestId?: string | number
  connectionKey?: string
  connectionId?: string
  method: string
  params: unknown
}

const serverNotificationMethods = new Set<string>(APP_SERVER_METHOD_GROUPS.serverNotifications)
const serverRequestMethods = new Set<string>(APP_SERVER_METHOD_GROUPS.serverRequests)

export function isKnownServerNotification(
  payload: RawNotificationPayload
): payload is KnownNotificationPayload {
  return serverNotificationMethods.has(payload.method)
}

export function isKnownServerRequest(
  payload: RawServerRequestPayload
): payload is KnownServerRequestPayload {
  return serverRequestMethods.has(payload.method)
}

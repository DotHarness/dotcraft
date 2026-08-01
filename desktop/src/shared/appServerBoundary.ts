import {
  APP_SERVER_METHOD_GROUPS,
  type ClientRequestMethods,
  type ServerNotificationMethods,
  type ServerRequestMethods
} from '@dotcraft/sdk/contracts'

export type AppServerRequestMethod = keyof ClientRequestMethods

export interface TypedAppServerRequestApi {
  sendRequest<M extends AppServerRequestMethod>(
    method: M,
    params: ClientRequestMethods[M]['params'],
    timeoutMs?: number | null
  ): Promise<ClientRequestMethods[M]['result']>
  sendRequestRaw(method: string, params?: unknown, timeoutMs?: number | null): Promise<unknown>
}

export type KnownNotificationPayload = {
  [M in keyof ServerNotificationMethods]: {
    method: M
    params: ServerNotificationMethods[M]['params']
    workspacePath?: string
    foreground?: boolean
  }
}[keyof ServerNotificationMethods]

export interface RawNotificationPayload {
  method: string
  params: unknown
  workspacePath?: string
  foreground?: boolean
}

export type KnownServerRequestPayload = {
  [M in keyof ServerRequestMethods]: {
    bridgeId: string
    method: M
    params: ServerRequestMethods[M]['params']
  }
}[keyof ServerRequestMethods]

export interface RawServerRequestPayload {
  bridgeId: string
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

import type { DesktopPluginHost } from '@dotcraft/plugin'
import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'

export type Rpc = DesktopPluginHost['appServer']

/** The server reports a capability it does not advertise as an unknown method. */
const METHOD_NOT_FOUND = -32601

interface RpcFailure {
  rpcCode?: number
  message?: string
  data?: { code?: string; fallbackText?: string }
}

function failure(error: unknown): RpcFailure {
  return error != null && typeof error === 'object' ? error as RpcFailure : {}
}

export function isUnsupported(error: unknown): boolean {
  return failure(error).rpcCode === METHOD_NOT_FOUND
}

export function errorCode(error: unknown): string | undefined {
  return failure(error).data?.code
}

export function errorMessage(error: unknown): string {
  const raw = failure(error)
  return raw.data?.fallbackText ?? raw.message ?? String(error)
}

export type RequestParams<M extends keyof ClientRequestMethods> = ClientRequestMethods[M]['params']
export type RequestResult<M extends keyof ClientRequestMethods> = ClientRequestMethods[M]['result']

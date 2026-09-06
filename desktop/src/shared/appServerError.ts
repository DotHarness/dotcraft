/**
 * Electron flattens a thrown Error to its message, so main returns AppServer failures
 * as an envelope and preload rebuilds an Error carrying the JSON-RPC code and `data`.
 * Imported by main, preload and the renderer, so no Node or Electron APIs.
 */

export interface AppServerErrorData {
  /** Stable domain code, e.g. `remote_workspace_busy`. */
  code?: string
  messageKey?: string
  params?: Record<string, unknown>
  /** English text to show when `messageKey` is unknown to this client. */
  fallbackText?: string
  [key: string]: unknown
}

export interface AppServerErrorFields {
  /** Symbolic SDK code, e.g. `turnInProgress`; matches `JsonRpcError.code`. */
  code?: string
  /** Numeric JSON-RPC code, e.g. `-32012`; matches `JsonRpcError.rpcCode`. */
  rpcCode?: number
  data?: AppServerErrorData
}

export interface AppServerErrorEnvelope extends AppServerErrorFields {
  __appServerError: true
  message: string
  name?: string
}

export interface AppServerRequestError extends Error, AppServerErrorFields {}

function optionalText(value: unknown): string | undefined {
  return typeof value === 'string' && value.trim().length > 0 ? value : undefined
}

/** Drop anything that cannot survive structured clone, including cyclic causes. */
function cloneableData(value: unknown): AppServerErrorData | undefined {
  if (value == null || typeof value !== 'object' || Array.isArray(value)) return undefined
  try {
    const copy: unknown = JSON.parse(JSON.stringify(value))
    return copy != null && typeof copy === 'object' && !Array.isArray(copy)
      ? (copy as AppServerErrorData)
      : undefined
  } catch {
    return undefined
  }
}

export function readAppServerErrorFields(error: unknown): AppServerErrorFields {
  if (error == null || typeof error !== 'object') return {}
  const raw = error as { code?: unknown; rpcCode?: unknown; data?: unknown }
  const data = cloneableData(raw.data)
  return {
    ...(optionalText(raw.code) ? { code: raw.code as string } : {}),
    ...(typeof raw.rpcCode === 'number' && Number.isFinite(raw.rpcCode)
      ? { rpcCode: raw.rpcCode }
      : {}),
    ...(data ? { data } : {})
  }
}

export function toAppServerErrorEnvelope(error: unknown): AppServerErrorEnvelope {
  const message = error instanceof Error ? error.message : String(error)
  const name = error instanceof Error ? optionalText(error.name) : undefined
  return {
    __appServerError: true,
    message,
    ...(name && name !== 'Error' ? { name } : {}),
    ...readAppServerErrorFields(error)
  }
}

function isAppServerErrorEnvelope(value: unknown): value is AppServerErrorEnvelope {
  return (
    value != null &&
    typeof value === 'object' &&
    (value as AppServerErrorEnvelope).__appServerError === true &&
    typeof (value as AppServerErrorEnvelope).message === 'string'
  )
}

export function fromAppServerErrorEnvelope(
  envelope: AppServerErrorEnvelope
): AppServerRequestError {
  const error = new Error(envelope.message) as AppServerRequestError
  if (envelope.name) error.name = envelope.name
  if (envelope.code !== undefined) error.code = envelope.code
  if (envelope.rpcCode !== undefined) error.rpcCode = envelope.rpcCode
  if (envelope.data !== undefined) error.data = envelope.data
  return error
}

/** Rethrows a failure envelope; any other value is the request's own result. */
export function unwrapAppServerResult<T>(outcome: unknown): T {
  if (isAppServerErrorEnvelope(outcome)) throw fromAppServerErrorEnvelope(outcome)
  return outcome as T
}

export type RemoteConnectionValidationCode =
  | 'missing-url'
  | 'invalid-url'
  | 'unsupported-protocol'

export interface RemoteConnectionSettingsDraft {
  url?: string
  token?: string
}

export interface ConnectionSettingsDraft {
  binarySource?: 'bundled' | 'path' | 'custom'
  appServerBinaryPath?: string
  connectionMode?: 'local' | 'remote'
  webSocket?: {
    host?: string
    port?: number
  }
  remote?: RemoteConnectionSettingsDraft
}

export type RemoteConnectionParseResult =
  | {
      ok: true
      wsUrl: string
      connectUrl: string
      token?: string
    }
  | {
      ok: false
      code: RemoteConnectionValidationCode
      message: string
    }

export function resolveRemoteWebSocketConfig(
  remote: RemoteConnectionSettingsDraft | undefined
): RemoteConnectionParseResult {
  const raw = remote?.url?.trim()
  if (!raw) {
    return {
      ok: false,
      code: 'missing-url',
      message: 'Remote WebSocket URL is required.'
    }
  }

  let parsed: URL
  try {
    parsed = new URL(raw)
  } catch {
    return {
      ok: false,
      code: 'invalid-url',
      message: 'Remote WebSocket URL is invalid.'
    }
  }

  if (parsed.protocol !== 'ws:' && parsed.protocol !== 'wss:') {
    return {
      ok: false,
      code: 'unsupported-protocol',
      message: 'Remote WebSocket URL must use ws:// or wss://.'
    }
  }

  const token = remote?.token?.trim() || undefined
  const connectUrl = appendTokenToWebSocketUrl(parsed.toString(), token)
  return token
    ? { ok: true, wsUrl: parsed.toString(), connectUrl, token }
    : { ok: true, wsUrl: parsed.toString(), connectUrl }
}

function appendTokenToWebSocketUrl(urlRaw: string, token: string | undefined): string {
  if (!token) return urlRaw
  const parsed = new URL(urlRaw)
  if (!parsed.searchParams.get('token')) {
    parsed.searchParams.set('token', token)
  }
  return parsed.toString()
}


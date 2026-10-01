import { findSseBoundary, type HubAppServerResponse, type HubEvent, type HubStatusResponse } from '@dotcraft/sdk/hub'

export interface RemoteHubClient {
  getStatus(timeoutMs?: number): Promise<HubStatusResponse>
  ensureAppServer(workspacePath: string): Promise<HubAppServerResponse>
  shutdown(): Promise<void>
  subscribeEvents(onEvent: (event: HubEvent) => void, signal: AbortSignal): Promise<void>
}

export interface RemoteHubClientOptions {
  baseUrl: string
  token: string
  clientName: string
  clientVersion: string
  fetchImpl?: typeof fetch
}

export class RemoteHubRequestError extends Error {
  constructor(readonly code: string, message: string) {
    super(message)
    this.name = 'RemoteHubRequestError'
  }
}

const DEFAULT_REQUEST_TIMEOUT_MS = 30_000

export function createRemoteHubClient(options: RemoteHubClientOptions): RemoteHubClient {
  const fetchImpl = options.fetchImpl ?? fetch
  const baseUrl = options.baseUrl.replace(/\/+$/, '')

  async function request<T>(path: string, init: RequestInit, timeoutMs = DEFAULT_REQUEST_TIMEOUT_MS): Promise<T> {
    const controller = new AbortController()
    const timer = setTimeout(() => controller.abort(), timeoutMs)
    try {
      const response = await fetchImpl(`${baseUrl}${path}`, {
        ...init,
        signal: controller.signal,
        headers: {
          'Content-Type': 'application/json',
          Authorization: `Bearer ${options.token}`,
          ...(init.headers ?? {})
        }
      })
      if (!response.ok) throw await toError(response)
      return (await response.json()) as T
    } catch (error) {
      if (error instanceof RemoteHubRequestError) throw error
      if (controller.signal.aborted) throw new RemoteHubRequestError('timeout', 'Remote Hub request timed out.')
      throw new RemoteHubRequestError('unreachable', error instanceof Error ? error.message : String(error))
    } finally {
      clearTimeout(timer)
    }
  }

  return {
    getStatus(timeoutMs = 5_000) {
      return request<HubStatusResponse>('/v1/status', { method: 'GET' }, timeoutMs)
    },
    ensureAppServer(workspacePath) {
      return request<HubAppServerResponse>('/v1/appservers/ensure', {
        method: 'POST',
        body: JSON.stringify({
          workspacePath,
          client: { name: options.clientName, version: options.clientVersion },
          startIfMissing: true
        })
      }, 120_000)
    },
    async shutdown() {
      await request<{ ok: boolean }>('/v1/shutdown', { method: 'POST' }, 15_000)
    },
    async subscribeEvents(onEvent, signal) {
      const response = await fetchImpl(`${baseUrl}/v1/events`, {
        headers: { Authorization: `Bearer ${options.token}` },
        signal
      })
      if (!response.ok || !response.body) throw await toError(response)
      const reader = response.body.getReader()
      const decoder = new TextDecoder()
      let buffer = ''
      while (!signal.aborted) {
        const { done, value } = await reader.read()
        if (done) break
        buffer += decoder.decode(value, { stream: true })
        let boundary = findSseBoundary(buffer)
        while (boundary) {
          const raw = buffer.slice(0, boundary.index)
          buffer = buffer.slice(boundary.index + boundary.sequence.length)
          const data = raw.split(/\r?\n/).find((line) => line.startsWith('data:'))?.slice('data:'.length).trim()
          if (data) {
            try {
              onEvent(JSON.parse(data) as HubEvent)
            } catch {
              void 0
            }
          }
          boundary = findSseBoundary(buffer)
        }
      }
    }
  }
}

async function toError(response: Response): Promise<RemoteHubRequestError> {
  try {
    const body = (await response.json()) as { error?: { code?: string; message?: string } }
    if (body.error?.code || body.error?.message) {
      return new RemoteHubRequestError(
        body.error.code ?? 'hubRequestFailed',
        body.error.message ?? `Remote Hub request failed with HTTP ${response.status}.`
      )
    }
  } catch {
    void 0
  }
  return new RemoteHubRequestError(
    response.status === 401 ? 'unauthorized' : 'hubRequestFailed',
    `Remote Hub request failed with HTTP ${response.status}.`
  )
}

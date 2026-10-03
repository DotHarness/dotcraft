export interface PinnedRequest {
  url: string
  method: 'GET' | 'POST' | 'DELETE'
  headers: Record<string, string>
  body?: string
  fingerprint: string
  tunnel?: string
}

export interface PinnedResponse {
  status: number
  body: string
}

export interface PinnedSocketOptions {
  url: string
  headers: Record<string, string>
  fingerprint: string
  tunnel?: string
}

export type PinnedSocketEvent =
  | { type: 'open'; id: string }
  | { type: 'message'; id: string; text: string }
  | { type: 'close'; id: string; code: number; reason: string }
  | { type: 'error'; id: string; code: string; message: string; status?: number | null }

export interface PinnedNative {
  request(request: PinnedRequest): Promise<PinnedResponse>
  openSocket(options: PinnedSocketOptions): string
  send(id: string, text: string): void
  close(id: string, code: number, reason: string): void
  subscribe(listener: (event: PinnedSocketEvent) => void): () => void
}

export const PIN_MISMATCH = 'ERR_PINNING_MISMATCH'
export const HTTP_REJECTED = 'ERR_HTTP'

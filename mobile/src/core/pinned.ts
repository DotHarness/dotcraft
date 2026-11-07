import type { PinnedRequest, PinnedResponse, PinnedSocketOptions } from '@dotcraft/sdk/gateway'

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

export const HTTP_REJECTED = 'ERR_HTTP'

import WebSocket from 'ws'
import { MAXIMUM_FRAME_BYTES, SCREEN_FRAME_HEADER_BYTES } from '../../shared/screenView'

export interface ScreenSocketTarget {
  url: string
  headers: Record<string, string>
}

export interface ScreenSocketHandlers {
  onOpen(): void
  onText(text: string): void
  onBinary(data: Uint8Array): void
  /** The HTTP status of a handshake the Hub refused; no `onClose` follows. */
  onHandshakeStatus(status: number): void
  onClose(code: number, reason: string): void
  onFailure(): void
}

export interface ScreenSocket {
  sendText(text: string): void
  close(): void
}

export type ScreenSocketFactory = (
  target: ScreenSocketTarget,
  handlers: ScreenSocketHandlers
) => ScreenSocket

export const createScreenSocket: ScreenSocketFactory = (target, handlers) => {
  const socket = new WebSocket(target.url, {
    headers: target.headers,
    perMessageDeflate: false,
    maxPayload: MAXIMUM_FRAME_BYTES + SCREEN_FRAME_HEADER_BYTES
  })
  let settled = false

  socket.on('open', () => handlers.onOpen())

  socket.on('message', (data, isBinary) => {
    if (isBinary) {
      handlers.onBinary(toBytes(data))
      return
    }
    handlers.onText(toBytes(data).toString())
  })

  socket.on('unexpected-response', (_request, response) => {
    settled = true
    handlers.onHandshakeStatus(response.statusCode ?? 0)
    socket.terminate()
  })

  socket.on('error', () => {
    if (settled) return
    settled = true
    handlers.onFailure()
  })

  socket.on('close', (code, reason) => {
    if (settled) return
    settled = true
    handlers.onClose(code, reason.toString())
  })

  return {
    sendText(text) {
      if (socket.readyState === WebSocket.OPEN) socket.send(text)
    },
    close() {
      settled = true
      if (socket.readyState === WebSocket.CONNECTING) socket.terminate()
      else socket.close()
    }
  }
}

function toBytes(data: WebSocket.RawData): Buffer {
  if (Buffer.isBuffer(data)) return data
  if (Array.isArray(data)) return Buffer.concat(data)
  return Buffer.from(data)
}

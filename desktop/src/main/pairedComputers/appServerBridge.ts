import { randomBytes, timingSafeEqual } from 'crypto'
import { createServer, type Server } from 'http'
import type { AddressInfo } from 'net'
import WebSocket, { WebSocketServer, type RawData } from 'ws'

export interface AppServerBridge {
  wsUrl: string
  port: number
  close(): void
}

const CLOSE_INTERNAL_ERROR = 1011

function closeCodeForSend(code: number): number {
  return code === 1005 || code === 1006 || code === 1015 ? 1000 : code
}

function relay(client: WebSocket, upstream: WebSocket): void {
  const pending: Array<{ data: RawData; isBinary: boolean }> = []

  client.on('message', (data, isBinary) => {
    if (upstream.readyState === WebSocket.OPEN) upstream.send(data, { binary: isBinary })
    else pending.push({ data, isBinary })
  })
  upstream.once('open', () => {
    for (const { data, isBinary } of pending.splice(0)) upstream.send(data, { binary: isBinary })
  })
  upstream.on('message', (data, isBinary) => {
    if (client.readyState === WebSocket.OPEN) client.send(data, { binary: isBinary })
  })

  client.once('close', (code, reason) => {
    if (upstream.readyState === WebSocket.OPEN) upstream.close(closeCodeForSend(code), reason)
    else upstream.terminate()
  })
  upstream.once('close', (code, reason) => {
    if (client.readyState === WebSocket.OPEN) client.close(closeCodeForSend(code), reason)
  })
  upstream.once('error', () => {
    if (client.readyState === WebSocket.OPEN) client.close(CLOSE_INTERNAL_ERROR, 'The computer could not be reached.')
  })
  client.once('error', () => upstream.terminate())
}

function secretMatches(actual: string | null, secret: string): boolean {
  if (!actual) return false
  const left = Buffer.from(actual)
  const right = Buffer.from(secret)
  return left.length === right.length && timingSafeEqual(left, right)
}

function listen(server: Server): Promise<number> {
  return new Promise((resolve, reject) => {
    server.once('error', reject)
    server.listen(0, '127.0.0.1', () => {
      server.off('error', reject)
      resolve((server.address() as AddressInfo).port)
    })
  })
}

export async function openAppServerBridge(dial: () => WebSocket): Promise<AppServerBridge> {
  const secret = randomBytes(32).toString('base64url')
  const sockets = new WebSocketServer({ noServer: true, perMessageDeflate: false, maxPayload: 0 })
  const server = createServer((_request, response) => {
    response.writeHead(404)
    response.end()
  })

  server.on('upgrade', (request, socket, head) => {
    const url = new URL(request.url ?? '/', 'ws://127.0.0.1')
    if (url.pathname !== '/ws' || !secretMatches(url.searchParams.get('token'), secret)) {
      socket.destroy()
      return
    }
    sockets.handleUpgrade(request, socket, head, (client) => relay(client, dial()))
  })

  const port = await listen(server)
  return {
    wsUrl: `ws://127.0.0.1:${port}/ws?token=${secret}`,
    port,
    close() {
      for (const client of sockets.clients) client.terminate()
      sockets.close()
      server.close()
    }
  }
}

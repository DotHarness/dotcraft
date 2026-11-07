import { createHash } from 'crypto'
import { request as httpsRequest } from 'https'
import { connect as tlsConnect, type TLSSocket } from 'tls'
import type { Duplex } from 'stream'
import WebSocket, { createWebSocketStream } from 'ws'
import {
  PIN_MISMATCH,
  type PinnedRequest,
  type PinnedResponse,
  type PinnedSocketOptions
} from '@dotcraft/sdk/gateway'

const DIRECT_CONNECT_TIMEOUT_MS = 3_000
const TUNNEL_CONNECT_TIMEOUT_MS = 12_000
const REQUEST_TIMEOUT_MS = 60_000

type ConnectionCallback = (error: Error | null, socket?: Duplex) => void

function peerFingerprint(socket: TLSSocket): string {
  const certificate = socket.getPeerX509Certificate()
  return certificate ? createHash('sha256').update(certificate.raw).digest('hex') : ''
}

function pinMismatch(): Error {
  return Object.assign(new Error('The computer presented a different certificate.'), { code: PIN_MISMATCH })
}

/** The socket is handed over only after the pin check, so no request byte reaches an unverified peer. */
function pinnedConnection(fingerprint: string, tunnel: string | undefined) {
  return (options: { host?: string | null; hostname?: string | null; port?: number | string | null }, done: ConnectionCallback): undefined => {
    let settled = false
    let relay: WebSocket | undefined
    let tls: TLSSocket | undefined

    function settle(error: Error | null, socket?: Duplex): void {
      if (settled) return
      settled = true
      clearTimeout(timer)
      if (error) {
        tls?.destroy()
        relay?.terminate()
      }
      done(error, socket)
    }

    const timer = setTimeout(
      () => settle(new Error('Connecting to the computer timed out.')),
      tunnel ? TUNNEL_CONNECT_TIMEOUT_MS : DIRECT_CONNECT_TIMEOUT_MS
    )

    function startTls(transport?: Duplex): void {
      const socket = tlsConnect({
        ...(transport
          ? { socket: transport }
          : { host: options.hostname ?? options.host ?? undefined, port: Number(options.port) }),
        rejectUnauthorized: false,
        checkServerIdentity: () => undefined
      })
      tls = socket
      socket.once('secureConnect', () => {
        if (peerFingerprint(socket) !== fingerprint.toLowerCase()) settle(pinMismatch())
        else settle(null, socket)
      })
      socket.once('error', (error) => settle(error))
      if (transport) {
        transport.once('error', (error) => socket.destroy(error))
        socket.once('close', () => transport.destroy())
      }
    }

    if (!tunnel) {
      startTls()
      return undefined
    }
    relay = new WebSocket(tunnel, { perMessageDeflate: false })
    relay.once('open', () => startTls(createWebSocketStream(relay as WebSocket)))
    relay.once('error', (error) => settle(error))
    return undefined
  }
}

export function pinnedRequest(request: PinnedRequest): Promise<PinnedResponse> {
  const url = new URL(request.url)
  return new Promise((resolve, reject) => {
    const outgoing = httpsRequest(
      {
        host: url.hostname,
        port: url.port,
        path: `${url.pathname}${url.search}`,
        method: request.method,
        headers: {
          ...request.headers,
          ...(request.body === undefined ? {} : { 'Content-Length': Buffer.byteLength(request.body) })
        },
        agent: undefined,
        timeout: REQUEST_TIMEOUT_MS,
        createConnection: pinnedConnection(request.fingerprint, request.tunnel) as never
      },
      (response) => {
        const chunks: Buffer[] = []
        response.on('data', (chunk: Buffer) => chunks.push(chunk))
        response.once('error', reject)
        response.once('end', () =>
          resolve({ status: response.statusCode ?? 0, body: Buffer.concat(chunks).toString('utf8') })
        )
      }
    )
    outgoing.once('timeout', () => outgoing.destroy(new Error('The computer did not answer in time.')))
    outgoing.once('error', reject)
    outgoing.end(request.body)
  })
}

export function openPinnedWebSocket(options: PinnedSocketOptions): WebSocket {
  return new WebSocket(options.url, {
    headers: options.headers,
    perMessageDeflate: false,
    maxPayload: 0,
    createConnection: pinnedConnection(options.fingerprint, options.tunnel) as never
  })
}

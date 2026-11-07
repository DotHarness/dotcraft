import { afterEach, describe, expect, it } from 'vitest'
import WebSocket, { WebSocketServer } from 'ws'
import { openAppServerBridge, type AppServerBridge } from '../pairedComputers/appServerBridge'

function connect(url: string): Promise<WebSocket> {
  return new Promise((resolve, reject) => {
    const socket = new WebSocket(url)
    socket.once('open', () => resolve(socket))
    socket.once('error', reject)
  })
}

function nextMessage(socket: WebSocket): Promise<{ text: string; isBinary: boolean }> {
  return new Promise((resolve) => {
    socket.once('message', (data, isBinary) => resolve({ text: data.toString(), isBinary }))
  })
}

function closed(socket: WebSocket): Promise<{ code: number; reason: string }> {
  return new Promise((resolve) => {
    socket.once('close', (code, reason) => resolve({ code, reason: reason.toString() }))
  })
}

describe('openAppServerBridge', () => {
  let bridge: AppServerBridge | undefined
  let upstreamServer: WebSocketServer | undefined

  afterEach(() => {
    bridge?.close()
    upstreamServer?.close()
    bridge = undefined
    upstreamServer = undefined
  })

  async function start(): Promise<{ url: string; port: number }> {
    upstreamServer = new WebSocketServer({ port: 0, host: '127.0.0.1' })
    await new Promise((resolve) => upstreamServer?.once('listening', resolve))
    return {
      port: (upstreamServer.address() as { port: number }).port,
      url: `ws://127.0.0.1:${(upstreamServer.address() as { port: number }).port}`
    }
  }

  it('refuses a local connection without the session secret', async () => {
    const { url } = await start()
    let dialed = 0
    bridge = await openAppServerBridge(() => {
      dialed += 1
      return new WebSocket(url)
    })
    const withoutSecret = bridge.wsUrl.split('?')[0]
    await expect(connect(withoutSecret)).rejects.toThrow()
    await expect(connect(`${withoutSecret}?token=guess`)).rejects.toThrow()
    expect(dialed).toBe(0)
  })

  it('relays both ways, keeps message types, and delivers what was sent before the upstream opened', async () => {
    const { url } = await start()
    const upstreamConnected = new Promise<WebSocket>((resolve) => upstreamServer?.once('connection', resolve))
    bridge = await openAppServerBridge(() => new WebSocket(url))
    const client = await connect(bridge.wsUrl)
    client.send('early')

    const upstream = await upstreamConnected
    const early = await nextMessage(upstream)
    expect(early).toEqual({ text: 'early', isBinary: false })

    const reply = nextMessage(client)
    upstream.send(Buffer.from('binary-reply'))
    expect(await reply).toEqual({ text: 'binary-reply', isBinary: true })
    client.close()
  })

  it('closes the local side with the gateway reason, and fails it when the computer cannot be reached', async () => {
    const { url } = await start()
    const upstreamConnected = new Promise<WebSocket>((resolve) => upstreamServer?.once('connection', resolve))
    bridge = await openAppServerBridge(() => new WebSocket(url))
    const client = await connect(bridge.wsUrl)
    const upstream = await upstreamConnected
    const closing = closed(client)
    upstream.close(1001, 'gatewayOff')
    expect(await closing).toEqual({ code: 1001, reason: 'gatewayOff' })
    bridge.close()

    bridge = await openAppServerBridge(() => new WebSocket('ws://127.0.0.1:1'))
    const orphan = await connect(bridge.wsUrl)
    expect((await closed(orphan)).code).toBe(1011)
  })
})

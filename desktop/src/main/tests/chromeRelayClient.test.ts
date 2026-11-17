import { createServer, type Server, type Socket } from 'node:net'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { afterEach, expect, it } from 'vitest'
import { BrowserUseBackendFrameDecoder, encodeBrowserUseBackendFrame } from '../browserUseBackendServer'
import { ChromeRelayClient } from '../chrome/chromeRelayClient'

const servers: Server[] = []

afterEach(async () => {
  await Promise.all(servers.splice(0).map((server) => new Promise((resolve) => server.close(resolve))))
})

function pipePath(name: string): string {
  const unique = `dotcraft-chrome-test-${name}-${process.pid}-${Math.random().toString(36).slice(2)}`
  return process.platform === 'win32' ? `\\\\.\\pipe\\${unique}` : join(tmpdir(), `${unique}.sock`)
}

async function relayServer(
  name: string,
  handle: (message: { id: number; method: string }, socket: Socket) => void
): Promise<string> {
  const path = pipePath(name)
  const server = createServer((socket) => {
    const decoder = new BrowserUseBackendFrameDecoder()
    socket.on('data', (chunk) => {
      for (const frame of decoder.push(chunk)) handle(JSON.parse(frame), socket)
    })
  })
  servers.push(server)
  await new Promise<void>((resolve) => server.listen(path, resolve))
  return path
}

const reply = (socket: Socket, payload: unknown) => socket.write(encodeBrowserUseBackendFrame(payload))

it('selects the compatible relay, forwards events, and fails pending calls when the relay closes', async () => {
  const stale = await relayServer('stale', (message, socket) => reply(socket, { id: message.id, result: { protocolVersion: 3 } }))
  let current: Socket | undefined
  const live = await relayServer('live', (message, socket) => {
    current = socket
    if (message.method === 'hello') {
      reply(socket, { id: message.id, result: { protocolVersion: 4 } })
      reply(socket, { event: 'tabs.removed', params: { tabId: 7 } })
    }
  })
  const client = new ChromeRelayClient({ listPipes: async () => [stale, live] })
  const events: unknown[] = []
  let disconnected = false
  client.on('event', (name, params) => events.push({ name, params }))
  client.on('disconnected', () => { disconnected = true })

  const pending = client.request('tabs.list')
  await expect.poll(() => current).toBeDefined()
  current!.destroy()

  await expect(pending).rejects.toThrow(/^BridgeDisconnected:/)
  expect(events).toEqual([{ name: 'tabs.removed', params: { tabId: 7 } }])
  expect(disconnected).toBe(true)
  expect(client.connected).toBe(false)
})

it('reports BridgeDisconnected when no relay answers', async () => {
  const client = new ChromeRelayClient({ listPipes: async () => [] })

  await expect(client.request('tabs.list')).rejects.toThrow(/^BridgeDisconnected:/)
})

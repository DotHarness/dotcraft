import net from 'node:net'
import os from 'node:os'
import path from 'node:path'
import { afterEach, describe, expect, it } from 'vitest'

type Frame = Record<string, any>

const { createNativeRelay, createRelayServer, encodeFrame, FrameDecoder } = await import(
  new URL('../../../resources/plugins/dotcraft-bundled/plugins/chrome/scripts/native-host.mjs', import.meta.url).href
)

const servers: net.Server[] = []

afterEach(async () => {
  await Promise.all(servers.splice(0).map((server) => new Promise((resolve) => server.close(resolve))))
})

function testPipePath(): string {
  const name = `dotcraft-chrome-test-${process.pid}-${Math.random().toString(36).slice(2)}`
  return process.platform === 'win32' ? `\\\\.\\pipe\\${name}` : path.join(os.tmpdir(), `${name}.sock`)
}

async function startRelay() {
  const toExtension: Frame[] = []
  const extensionWaiters: Array<(message: Frame) => void> = []
  const relay = createNativeRelay({
    sendNative: (message: Frame) => {
      toExtension.push(message)
      extensionWaiters.shift()?.(message)
    },
    openSettings: async () => undefined
  })
  const pipePath = testPipePath()
  const server = createRelayServer(relay, pipePath)
  servers.push(server)
  await new Promise<void>((resolve) => server.listen(pipePath, resolve))
  return {
    relay,
    toExtension,
    nextExtensionMessage: () => new Promise<Frame>((resolve) => extensionWaiters.push(resolve)),
    async connect() {
      const registered = new Promise((resolve) => server.once('connection', resolve))
      const client = await connectClient(pipePath)
      await registered
      return client
    }
  }
}

async function connectClient(pipePath: string) {
  const socket = net.createConnection(pipePath)
  await new Promise<void>((resolve) => socket.once('connect', resolve))
  const decoder = new FrameDecoder()
  const frames: Frame[] = []
  const waiters: Array<(frame: Frame) => void> = []
  socket.on('data', (chunk) => {
    for (const frame of decoder.push(chunk)) {
      frames.push(frame)
      waiters.shift()?.(frame)
    }
  })
  return {
    socket,
    frames,
    send: (frame: Frame) => socket.write(encodeFrame(frame)),
    next: () => new Promise<Frame>((resolve) => waiters.push(resolve)),
    close: () => new Promise<void>((resolve) => { socket.once('close', () => resolve()); socket.end() })
  }
}

describe('Chrome native host relay', () => {
  it('routes responses to the requesting client when ids collide', async () => {
    const host = await startRelay()
    const first = await host.connect()
    const second = await host.connect()

    const firstForward = host.nextExtensionMessage()
    first.send({ id: 1, method: 'tabs.list', params: {} })
    const forwardedFirst = await firstForward
    const secondForward = host.nextExtensionMessage()
    second.send({ id: 1, method: 'hello' })
    const forwardedSecond = await secondForward

    expect(forwardedFirst).toEqual({ type: 'relay-request', id: forwardedFirst.id, method: 'tabs.list', params: {} })
    expect(forwardedSecond).toMatchObject({ type: 'relay-request', method: 'hello', params: {} })
    expect(forwardedSecond.id).not.toBe(forwardedFirst.id)

    const secondReply = second.next()
    host.relay.handleExtensionMessage({ type: 'relay-response', id: forwardedSecond.id, result: { protocolVersion: 4 } })
    expect(await secondReply).toEqual({ id: 1, result: { protocolVersion: 4 } })
    const firstReply = first.next()
    host.relay.handleExtensionMessage({ type: 'relay-response', id: forwardedFirst.id, error: { message: 'TabStale: gone' } })
    expect(await firstReply).toEqual({ id: 1, error: { message: 'TabStale: gone' } })

    await Promise.all([first.close(), second.close()])
  })

  it('broadcasts extension events to every client', async () => {
    const host = await startRelay()
    const first = await host.connect()
    const second = await host.connect()
    const events = Promise.all([first.next(), second.next()])

    host.relay.handleExtensionMessage({ type: 'relay-event', event: 'debugger.detached', params: { tabId: 3, reason: 'canceled_by_user' } })

    expect(await events).toEqual([
      { event: 'debugger.detached', params: { tabId: 3, reason: 'canceled_by_user' } },
      { event: 'debugger.detached', params: { tabId: 3, reason: 'canceled_by_user' } }
    ])
    await Promise.all([first.close(), second.close()])
  })

  it('releases the extension when the last client disconnects', async () => {
    const host = await startRelay()
    const first = await host.connect()
    const second = await host.connect()

    await first.close()
    expect(host.toExtension).toEqual([])
    const released = host.nextExtensionMessage()
    await second.close()

    expect(await released).toEqual({ type: 'clients-released' })
  })

  it('fails pending requests once the extension port ends', async () => {
    const host = await startRelay()
    const client = await host.connect()
    const forwarded = host.nextExtensionMessage()
    client.send({ id: 7, method: 'debugger.send', params: { tabId: 1, method: 'Page.enable' } })
    await forwarded

    const pending = client.next()
    host.relay.extensionEnded()
    expect(await pending).toEqual({ id: 7, error: { message: 'BridgeDisconnected: the DotCraft Chrome extension disconnected.' } })
    await client.close()
  })
})

import { existsSync } from 'node:fs'
import { createConnection, type Socket } from 'node:net'
import { join } from 'node:path'
import { tmpdir } from 'node:os'
import { afterEach, describe, expect, it } from 'vitest'
import {
  BrowserUseBackendError,
  BrowserUseBackendFrameDecoder,
  BrowserUseBackendServer,
  encodeBrowserUseBackendFrame,
  isAllowedBrowserUsePipePath
} from '../browserUseBackendServer'

const servers: BrowserUseBackendServer[] = []

afterEach(async () => {
  await Promise.all(servers.map((server) => server.close()))
  servers.length = 0
})

function createTestServer(): BrowserUseBackendServer {
  const server = new BrowserUseBackendServer({
    async handleBrowserUseBackendRequest(method, params, context) {
      if (method === 'ping') return 'pong'
      if (method === 'echoRequestId') return { requestId: context.requestId }
      if (method === 'getInfo') {
        return {
          id: 'iab',
          name: 'DotCraft In-App Browser',
          type: 'iab',
          metadata: { dotcraftSessionId: params.session_id }
        }
      }
      throw BrowserUseBackendError.methodNotFound(method)
    }
  })
  servers.push(server)
  return server
}

async function connect(address: string): Promise<Socket> {
  return await new Promise<Socket>((resolveConnection, rejectConnection) => {
    const socket = createConnection(address)
    socket.once('connect', () => resolveConnection(socket))
    socket.once('error', rejectConnection)
  })
}

async function rpc(socket: Socket, method: string, params: Record<string, unknown> = {}): Promise<Record<string, unknown>> {
  const decoder = new BrowserUseBackendFrameDecoder()
  return await new Promise((resolveRpc, rejectRpc) => {
    const onData = (chunk: Buffer) => {
      for (const frame of decoder.push(chunk)) {
        socket.off('data', onData)
        resolveRpc(JSON.parse(frame) as Record<string, unknown>)
      }
    }
    socket.on('data', onData)
    socket.once('error', rejectRpc)
    socket.write(encodeBrowserUseBackendFrame({
      jsonrpc: '2.0',
      id: 1,
      method,
      params
    }))
  })
}

describe('BrowserUseBackendFrameDecoder', () => {
  it('decodes multiple frames and partial frames', () => {
    const first = encodeBrowserUseBackendFrame({ id: 1, result: 'one' })
    const second = encodeBrowserUseBackendFrame({ id: 2, result: 'two' })
    const decoder = new BrowserUseBackendFrameDecoder()

    expect(decoder.push(Buffer.concat([first, second.subarray(0, 3)]))).toHaveLength(1)
    const tail = decoder.push(second.subarray(3))

    expect(tail).toHaveLength(1)
    expect(JSON.parse(tail[0])).toEqual({ id: 2, result: 'two' })
  })
})

describe('BrowserUseBackendServer', () => {
  it('responds to JSON-RPC ping and getInfo', async () => {
    const server = createTestServer()
    const address = await server.ensureStarted()
    const socket = await connect(address)

    await expect(rpc(socket, 'ping')).resolves.toMatchObject({ result: 'pong' })
    await expect(rpc(socket, 'getInfo', { session_id: 'thread-1', turn_id: 'turn-1' })).resolves.toMatchObject({
      result: {
        id: 'iab',
        metadata: { dotcraftSessionId: 'thread-1' }
      }
    })

    socket.destroy()
  })

  it('returns a JSON-RPC error for unknown methods', async () => {
    const server = createTestServer()
    const socket = await connect(await server.ensureStarted())

    await expect(rpc(socket, 'missingMethod')).resolves.toMatchObject({
      error: {
        code: -32601,
        message: expect.stringContaining('missingMethod')
      }
    })

    socket.destroy()
  })

  it('passes JSON-RPC request id to the backend handler', async () => {
    const server = createTestServer()
    const socket = await connect(await server.ensureStarted())

    await expect(rpc(socket, 'echoRequestId')).resolves.toMatchObject({
      result: { requestId: 1 }
    })

    socket.destroy()
  })

  it('allows only DotCraft browser-use pipe namespaces', () => {
    expect(isAllowedBrowserUsePipePath('\\\\.\\pipe\\dotcraft-browser-use-dotcraft-123-abc', 'win32')).toBe(true)
    expect(isAllowedBrowserUsePipePath('\\\\.\\pipe\\dotcraft-browser-use-other-123', 'win32')).toBe(false)

    const allowedUnix = join(tmpdir(), 'dotcraft-browser-use', 'dotcraft-123-abc.sock')
    const rejectedUnix = join(tmpdir(), 'dotcraft-browser-use-dotcraft-123-abc.sock')
    expect(isAllowedBrowserUsePipePath(allowedUnix, 'linux')).toBe(true)
    expect(isAllowedBrowserUsePipePath(rejectedUnix, 'linux')).toBe(false)
  })

  it('disconnects sockets and cleans Unix socket files on close', async () => {
    const server = createTestServer()
    const address = await server.ensureStarted()
    const socket = await connect(address)
    const closed = new Promise<void>((resolveClose) => {
      socket.once('close', () => resolveClose())
    })

    await server.close()
    await closed

    const closedSocket = socket as Socket & { closed?: boolean }
    expect(closedSocket.closed === true || socket.destroyed).toBe(true)
    if (process.platform !== 'win32') {
      expect(existsSync(address)).toBe(false)
    }
  })
})

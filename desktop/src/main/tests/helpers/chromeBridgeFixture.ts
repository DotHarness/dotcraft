import net from 'node:net'
import os from 'node:os'
import path from 'node:path'
type BridgeRequest = {
  id: number
  kind: 'command' | 'cancel'
  commandId?: string
  method: string
  params: Record<string, unknown>
  browserSession?: Record<string, unknown>
  timeoutMs?: number
  reason?: string
}

function encodeFrame(message: unknown) {
  const body = Buffer.from(JSON.stringify(message), 'utf8')
  const header = Buffer.alloc(4)
  header.writeUInt32LE(body.length, 0)
  return Buffer.concat([header, body])
}

class FrameDecoder {
  private buffer = Buffer.alloc(0)

  push(chunk: Buffer) {
    this.buffer = Buffer.concat([this.buffer, chunk])
    const frames: BridgeRequest[] = []
    while (this.buffer.length >= 4) {
      const length = this.buffer.readUInt32LE(0)
      if (this.buffer.length < length + 4) break
      const body = this.buffer.subarray(4, 4 + length)
      this.buffer = this.buffer.subarray(4 + length)
      frames.push(JSON.parse(body.toString('utf8')))
    }
    return frames
  }
}

export function createPipePath() {
  return process.platform === 'win32'
    ? `\\\\.\\pipe\\dotcraft-chrome-test-${process.pid}-${Date.now()}-${Math.random().toString(36).slice(2)}`
    : path.join(os.tmpdir(), `dotcraft-chrome-test-${process.pid}-${Date.now()}-${Math.random().toString(36).slice(2)}.sock`)
}

export function createMockBridge(handler: (request: BridgeRequest) => unknown | Promise<unknown>) {
  const requests: BridgeRequest[] = []
  const cancels: BridgeRequest[] = []
  const sockets = new Set<net.Socket>()
  const pipePath = createPipePath()
  const server = net.createServer((socket) => {
    sockets.add(socket)
    const decoder = new FrameDecoder()
    socket.on('data', (chunk) => {
      for (const request of decoder.push(chunk)) {
        if (request.kind === 'command' && request.method === 'getInfo') {
          socket.write(encodeFrame({
            id: request.id,
            ok: true,
            result: { backendId: 'chrome-extension', protocolVersion: 3, supportsCommandCancel: true }
          }))
          continue
        }
        if (request.kind === 'cancel') {
          cancels.push(request)
          socket.write(encodeFrame({ id: request.id, ok: true, result: { ok: true } }))
          continue
        }
        if (request.kind === 'command') {
          requests.push(request)
          void (async () => {
            try {
              const result = await handler(request)
              socket.write(encodeFrame({ id: request.id, ok: true, result }))
            } catch (error) {
              socket.write(encodeFrame({
                id: request.id,
                ok: false,
                error: { message: error instanceof Error ? error.message : String(error) }
              }))
            }
          })()
        }
      }
    })
    socket.on('close', () => sockets.delete(socket))
  })

  return {
    requests,
    cancels,
    pipePath,
    async listen() {
      await new Promise<void>((resolve) => server.listen(pipePath, resolve))
      return pipePath
    },
    async close() {
      for (const socket of sockets) socket.destroy()
      await new Promise<void>((resolve) => server.close(() => resolve()))
    }
  }
}

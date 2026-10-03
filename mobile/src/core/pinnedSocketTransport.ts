import { TransportClosed, type Transport } from '@dotcraft/sdk/wire'
import type { PinnedSocketOptions } from './pinned'
import type { PinnedSockets, SocketEnd, SocketHandle } from './sockets'

export class SocketOpenError extends Error {
  constructor(readonly end: SocketEnd) {
    super(end.kind === 'failed' ? end.message : `The socket closed before it opened (${end.code}).`)
    this.name = 'SocketOpenError'
  }
}

type Reader = { resolve: (message: Record<string, unknown>) => void; reject: (error: unknown) => void }

export class PinnedSocketTransport implements Transport {
  private readonly frames: Record<string, unknown>[] = []
  private readers: Reader[] = []
  private ended: SocketEnd | null = null
  private endListener: ((end: SocketEnd) => void) | null = null
  private handle!: SocketHandle

  private constructor() {}

  static open(sockets: PinnedSockets, options: PinnedSocketOptions): Promise<PinnedSocketTransport> {
    const transport = new PinnedSocketTransport()
    return new Promise((resolve, reject) => {
      transport.handle = sockets.open(options, {
        open: () => resolve(transport),
        message: (text) => transport.receive(text),
        end: (end) => {
          reject(new SocketOpenError(end))
          transport.finish(end)
        },
      })
    })
  }

  onEnd(listener: (end: SocketEnd) => void): void {
    if (this.ended) listener(this.ended)
    else this.endListener = listener
  }

  async readMessage(): Promise<Record<string, unknown>> {
    const frame = this.frames.shift()
    if (frame) return frame
    if (this.ended) throw new TransportClosed()
    return await new Promise((resolve, reject) => this.readers.push({ resolve, reject }))
  }

  async writeMessage(message: Record<string, unknown>): Promise<void> {
    if (this.ended) throw new TransportClosed()
    this.handle.send(JSON.stringify(message))
  }

  async close(): Promise<void> {
    if (this.ended) return
    this.handle.close(1000, '')
    this.finish({ kind: 'closed', code: 1000, reason: '' })
  }

  private receive(text: string): void {
    const message = JSON.parse(text) as Record<string, unknown>
    const reader = this.readers.shift()
    if (reader) reader.resolve(message)
    else this.frames.push(message)
  }

  private finish(end: SocketEnd): void {
    if (this.ended) return
    this.ended = end
    this.endListener?.(end)
    for (const reader of this.readers.splice(0)) reader.reject(new TransportClosed())
  }
}

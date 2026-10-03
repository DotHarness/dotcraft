import type { PinnedNative, PinnedSocketEvent, PinnedSocketOptions } from './pinned'

export type SocketEnd =
  | { kind: 'closed'; code: number; reason: string }
  | { kind: 'failed'; code: string; message: string; status: number | null }

export interface SocketHandlers {
  open(): void
  message(text: string): void
  end(end: SocketEnd): void
}

export interface SocketHandle {
  send(text: string): void
  close(code?: number, reason?: string): void
}

export class PinnedSockets {
  private readonly handlers = new Map<string, SocketHandlers>()
  private unsubscribe: (() => void) | null = null

  constructor(private readonly native: PinnedNative) {}

  open(options: PinnedSocketOptions, handlers: SocketHandlers): SocketHandle {
    this.unsubscribe ??= this.native.subscribe((event) => this.dispatch(event))
    const id = this.native.openSocket(options)
    this.handlers.set(id, handlers)
    return {
      send: (text) => this.native.send(id, text),
      close: (code = 1000, reason = '') => this.native.close(id, code, reason),
    }
  }

  dispose(): void {
    this.unsubscribe?.()
    this.unsubscribe = null
    this.handlers.clear()
  }

  private dispatch(event: PinnedSocketEvent): void {
    const handlers = this.handlers.get(event.id)
    if (!handlers) return
    switch (event.type) {
      case 'open':
        handlers.open()
        return
      case 'message':
        handlers.message(event.text)
        return
      case 'close':
        this.handlers.delete(event.id)
        handlers.end({ kind: 'closed', code: event.code, reason: event.reason })
        return
      case 'error':
        this.handlers.delete(event.id)
        handlers.end({ kind: 'failed', code: event.code, message: event.message, status: event.status ?? null })
    }
  }
}

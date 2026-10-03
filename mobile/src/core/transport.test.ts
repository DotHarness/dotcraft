import { DotCraftWireClient, TransportClosed } from '@dotcraft/sdk/wire'
import { describe, expect, it } from 'vitest'
import type { PinnedNative, PinnedSocketEvent, PinnedSocketOptions } from './pinned'
import { PinnedSocketTransport, SocketOpenError } from './pinnedSocketTransport'
import { PinnedSockets } from './sockets'

class ScriptedNative implements PinnedNative {
  readonly sent: string[] = []
  readonly closed: { code: number; reason: string }[] = []
  private listener: ((event: PinnedSocketEvent) => void) | null = null

  request(): never {
    throw new Error('unused')
  }

  openSocket(): string {
    return 'socket-1'
  }

  send(_id: string, text: string): void {
    this.sent.push(text)
  }

  close(_id: string, code: number, reason: string): void {
    this.closed.push({ code, reason })
  }

  subscribe(listener: (event: PinnedSocketEvent) => void): () => void {
    this.listener = listener
    return () => {
      this.listener = null
    }
  }

  emit(event: Omit<PinnedSocketEvent, 'id'> & Record<string, unknown>): void {
    this.listener?.({ id: 'socket-1', ...event } as PinnedSocketEvent)
  }
}

const options: PinnedSocketOptions = { url: 'wss://10.0.0.2:47610/m/events', headers: { Authorization: 'Bearer c' }, fingerprint: 'f'.repeat(64) }

async function openTransport(native: ScriptedNative): Promise<PinnedSocketTransport> {
  const opening = PinnedSocketTransport.open(new PinnedSockets(native), options)
  native.emit({ type: 'open' })
  return await opening
}

describe('pinned socket transport', () => {
  it('rejects the open with the native failure, including the HTTP status of a refused upgrade', async () => {
    const native = new ScriptedNative()
    const opening = PinnedSocketTransport.open(new PinnedSockets(native), options)
    native.emit({ type: 'error', code: 'ERR_HTTP', message: 'Conflict', status: 409 })
    const error = await opening.catch((caught: unknown) => caught)
    expect(error).toBeInstanceOf(SocketOpenError)
    expect((error as SocketOpenError).end).toEqual({ kind: 'failed', code: 'ERR_HTTP', message: 'Conflict', status: 409 })
  })

  it('ends pending reads with TransportClosed and reports the close reason', async () => {
    const native = new ScriptedNative()
    const transport = await openTransport(native)
    const ends: unknown[] = []
    transport.onEnd((end) => ends.push(end))
    const reading = transport.readMessage()
    native.emit({ type: 'close', code: 1001, reason: 'gatewayOff' })
    await expect(reading).rejects.toBeInstanceOf(TransportClosed)
    expect(ends).toEqual([{ kind: 'closed', code: 1001, reason: 'gatewayOff' }])
    await expect(transport.writeMessage({})).rejects.toBeInstanceOf(TransportClosed)
  })

  it('carries a full wire client handshake and server requests', async () => {
    const native = new ScriptedNative()
    const transport = await openTransport(native)
    const client = new DotCraftWireClient(transport)
    client.registerServerRequestHandler('item/approval/request', () => ({ decision: 'accept' }))
    const initializing = client.initialize({ clientName: 'dotcraft-mobile', clientVersion: '0.8.0', approvalSupport: true, requestUserInputSupport: true })
    await new Promise((resolve) => setTimeout(resolve, 0))
    const initialize = JSON.parse(native.sent[0]) as { id: number; method: string; params: { capabilities: Record<string, unknown> } }
    expect(initialize.method).toBe('initialize')
    expect(initialize.params.capabilities).toMatchObject({ approvalSupport: true, requestUserInputSupport: true, streamingSupport: true })
    native.emit({ type: 'message', text: JSON.stringify({ jsonrpc: '2.0', id: initialize.id, result: { serverInfo: {}, capabilities: {} } }) })
    await initializing
    expect(JSON.parse(native.sent[1])).toMatchObject({ method: 'initialized' })
    native.emit({ type: 'message', text: JSON.stringify({ jsonrpc: '2.0', id: 'srv-1', method: 'item/approval/request', params: {} }) })
    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(JSON.parse(native.sent[2])).toEqual({ jsonrpc: '2.0', id: 'srv-1', result: { decision: 'accept' } })
    await client.stop()
    expect(native.closed).toEqual([{ code: 1000, reason: '' }])
  })
})

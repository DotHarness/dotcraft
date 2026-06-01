import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { Readable, Writable, PassThrough } from 'stream'
import { INITIALIZE_REQUEST_TIMEOUT_MS, WireProtocolClient } from '../WireProtocolClient'

function makeStreams(): { toServer: PassThrough; fromServer: PassThrough } {
  return {
    toServer: new PassThrough(),   // client writes here (stdin)
    fromServer: new PassThrough()  // server writes here (stdout)
  }
}

describe('WireProtocolClient', () => {
  let client: WireProtocolClient
  let toServer: PassThrough
  let fromServer: PassThrough

  beforeEach(() => {
    ;({ toServer, fromServer } = makeStreams())
    client = new WireProtocolClient(fromServer as unknown as Readable, toServer as unknown as Writable)
  })

  afterEach(() => {
    client.dispose()
    fromServer.destroy()
    toServer.destroy()
  })

  // ─── JSONL serialization ────────────────────────────────────────────────────

  it('serializes a request as valid JSONL (one JSON object per line)', async () => {
    const written: string[] = []
    toServer.on('data', (chunk: Buffer) => {
      written.push(...chunk.toString('utf8').split('\n').filter(Boolean))
    })

    // Don't await — server never replies in this test
    client.sendRequest('thread/start', { workspaceId: 'ws-1' }, 100).catch(() => {})

    await new Promise((r) => setTimeout(r, 10))

    expect(written).toHaveLength(1)
    const parsed = JSON.parse(written[0])
    expect(parsed).toMatchObject({
      jsonrpc: '2.0',
      method: 'thread/start',
      params: { workspaceId: 'ws-1' }
    })
    expect(typeof parsed.id).toBe('number')
  })

  it('serializes a notification as JSONL without an id field', async () => {
    const written: string[] = []
    toServer.on('data', (chunk: Buffer) => {
      written.push(...chunk.toString('utf8').split('\n').filter(Boolean))
    })

    await client.sendNotification('initialized', {})

    expect(written).toHaveLength(1)
    const parsed = JSON.parse(written[0])
    expect(parsed.method).toBe('initialized')
    expect(parsed.id).toBeUndefined()
  })

  // ─── Response correlation ────────────────────────────────────────────────────

  it('correlates a response to the correct pending request by id', async () => {
    let capturedId: number | null = null
    toServer.on('data', (chunk: Buffer) => {
      const line = chunk.toString('utf8').trim()
      if (!line) return
      capturedId = JSON.parse(line).id
    })

    const responsePromise = client.sendRequest<{ threadId: string }>('thread/start')

    // Wait a tick for the request to be written
    await new Promise((r) => setTimeout(r, 10))

    // Simulate server response with matching id
    fromServer.push(
      JSON.stringify({ jsonrpc: '2.0', id: capturedId, result: { threadId: 'tid-42' } }) + '\n'
    )

    const result = await responsePromise
    expect(result.threadId).toBe('tid-42')
  })

  it('rejects the promise when server returns an error response', async () => {
    let capturedId: number | null = null
    toServer.on('data', (chunk: Buffer) => {
      const line = chunk.toString('utf8').trim()
      if (!line) return
      capturedId = JSON.parse(line).id
    })

    const responsePromise = client.sendRequest('thread/start')

    await new Promise((r) => setTimeout(r, 10))

    fromServer.push(
      JSON.stringify({
        jsonrpc: '2.0',
        id: capturedId,
        error: { code: -32600, message: 'Invalid request' }
      }) + '\n'
    )

    await expect(responsePromise).rejects.toThrow('Invalid request')
  })

  it('handles multiple concurrent requests with distinct ids', async () => {
    const ids: number[] = []
    toServer.on('data', (chunk: Buffer) => {
      chunk
        .toString('utf8')
        .split('\n')
        .filter(Boolean)
        .forEach((line) => ids.push(JSON.parse(line).id))
    })

    const p1 = client.sendRequest<{ n: number }>('method/a')
    const p2 = client.sendRequest<{ n: number }>('method/b')
    const p3 = client.sendRequest<{ n: number }>('method/c')

    await new Promise((r) => setTimeout(r, 10))
    expect(new Set(ids).size).toBe(3)

    // Resolve out of order
    fromServer.push(JSON.stringify({ jsonrpc: '2.0', id: ids[1], result: { n: 2 } }) + '\n')
    fromServer.push(JSON.stringify({ jsonrpc: '2.0', id: ids[0], result: { n: 1 } }) + '\n')
    fromServer.push(JSON.stringify({ jsonrpc: '2.0', id: ids[2], result: { n: 3 } }) + '\n')

    const [r1, r2, r3] = await Promise.all([p1, p2, p3])
    expect(r1.n).toBe(1)
    expect(r2.n).toBe(2)
    expect(r3.n).toBe(3)
  })

  // ─── Notification dispatch ───────────────────────────────────────────────────

  it('dispatches a notification to registered callbacks', async () => {
    const received: { method: string; params: unknown }[] = []
    client.onNotification((method, params) => {
      received.push({ method, params })
    })

    fromServer.push(
      JSON.stringify({ jsonrpc: '2.0', method: 'turn/started', params: { turnId: 't-1' } }) + '\n'
    )

    await new Promise((r) => setTimeout(r, 10))

    expect(received).toHaveLength(1)
    expect(received[0].method).toBe('turn/started')
    expect((received[0].params as { turnId: string }).turnId).toBe('t-1')
  })

  /**
   * One JSONL notification line must invoke each registered callback exactly once.
   * Guards against accidental double-dispatch regressions (would duplicate streaming deltas in the UI).
   */
  it('dispatches exactly once per JSONL line for a single subscriber', async () => {
    let callCount = 0
    client.onNotification((method, params) => {
      if (method === 'item/agentMessage/delta') {
        callCount += 1
        expect((params as { delta?: string }).delta).toBe('x')
      }
    })

    fromServer.push(
      JSON.stringify({
        jsonrpc: '2.0',
        method: 'item/agentMessage/delta',
        params: { delta: 'x', turnId: 'turn_1' }
      }) + '\n'
    )

    await new Promise((r) => setTimeout(r, 10))

    expect(callCount).toBe(1)
  })

  it('calls all registered notification callbacks', async () => {
    const calls1: string[] = []
    const calls2: string[] = []
    client.onNotification((method) => calls1.push(method))
    client.onNotification((method) => calls2.push(method))

    fromServer.push(JSON.stringify({ jsonrpc: '2.0', method: 'item/started', params: {} }) + '\n')

    await new Promise((r) => setTimeout(r, 10))

    expect(calls1).toEqual(['item/started'])
    expect(calls2).toEqual(['item/started'])
  })

  it('unsubscribes a notification callback when the returned function is called', async () => {
    const received: string[] = []
    const unsub = client.onNotification((method) => received.push(method))

    fromServer.push(JSON.stringify({ jsonrpc: '2.0', method: 'event/a', params: {} }) + '\n')
    await new Promise((r) => setTimeout(r, 10))

    unsub()

    fromServer.push(JSON.stringify({ jsonrpc: '2.0', method: 'event/b', params: {} }) + '\n')
    await new Promise((r) => setTimeout(r, 10))

    expect(received).toEqual(['event/a'])
  })

  // ─── Server-initiated requests ───────────────────────────────────────────────

  it('dispatches server-initiated requests to the registered handler and sends a response', async () => {
    const responseLines: string[] = []
    toServer.on('data', (chunk: Buffer) => {
      chunk
        .toString('utf8')
        .split('\n')
        .filter(Boolean)
        .forEach((line) => responseLines.push(line))
    })

    client.onServerRequest(async (method, _params) => {
      expect(method).toBe('item/approval/request')
      return { decision: 'accept' }
    })

    fromServer.push(
      JSON.stringify({
        jsonrpc: '2.0',
        id: 99,
        method: 'item/approval/request',
        params: { operation: 'npm test' }
      }) + '\n'
    )

    await new Promise((r) => setTimeout(r, 20))

    expect(responseLines).toHaveLength(1)
    const response = JSON.parse(responseLines[0])
    expect(response).toMatchObject({
      jsonrpc: '2.0',
      id: 99,
      result: { decision: 'accept' }
    })
  })

  // ─── Timeout ─────────────────────────────────────────────────────────────────

  it('rejects with a timeout error when no response is received in time', async () => {
    await expect(
      client.sendRequest('slow/method', undefined, 50)
    ).rejects.toThrow(/timed out/)
  })

  it('does not time out the initialize handshake while AppServer is still starting', async () => {
    vi.useFakeTimers()
    try {
      expect(INITIALIZE_REQUEST_TIMEOUT_MS).toBeNull()

      const lines: string[] = []
      toServer.on('data', (chunk: Buffer) => {
        chunk
          .toString('utf8')
          .split('\n')
          .filter(Boolean)
          .forEach((line) => lines.push(line))
      })

      let rejected = false
      const initPromise = client.initialize()
      initPromise.catch(() => {
        rejected = true
      })

      await vi.advanceTimersByTimeAsync(10_000)
      expect(rejected).toBe(false)

      const initReq = JSON.parse(lines[0])
      fromServer.push(
        JSON.stringify({
          jsonrpc: '2.0',
          id: initReq.id,
          result: {
            serverInfo: { name: 'dotcraft', version: '0.2.0' },
            capabilities: { threadManagement: true }
          }
        }) + '\n'
      )

      await expect(initPromise).resolves.toMatchObject({
        serverInfo: { name: 'dotcraft', version: '0.2.0' },
        capabilities: { threadManagement: true }
      })
    } finally {
      vi.useRealTimers()
    }
  })

  // ─── Initialize handshake ────────────────────────────────────────────────────

  it('sends initialize request then initialized notification in sequence', async () => {
    const lines: string[] = []
    toServer.on('data', (chunk: Buffer) => {
      chunk
        .toString('utf8')
        .split('\n')
        .filter(Boolean)
        .forEach((line) => lines.push(line))
    })

    let capturedId: number | null = null
    toServer.once('data', (chunk: Buffer) => {
      const line = chunk.toString('utf8').trim()
      if (line) capturedId = JSON.parse(line).id
    })

    const initPromise = client.initialize()

    await new Promise((r) => setTimeout(r, 10))

    // First message should be the initialize request
    const initReq = JSON.parse(lines[0])
    expect(initReq.method).toBe('initialize')
    expect(initReq.params.clientInfo.name).toBe('dotcraft-desktop')
    expect(initReq.params.capabilities.approvalSupport).toBe(true)
    expect(initReq.params.capabilities.toolExecutionLifecycle).toBe(true)
    expect(initReq.params.capabilities.backgroundTerminals).toBe(true)
    expect(initReq.params.capabilities.nodeRepl).toEqual({
      backend: 'desktop-node'
    })
    expect(initReq.params.capabilities.browserUse).toEqual({
      backend: 'desktop-iab',
      backends: ['desktop-iab'],
      protocolVersion: 2,
      supportsCancel: true,
      browserSessionProtocolVersion: 1,
      defaultCommandTimeoutMs: 10000,
      maxCommandTimeoutMs: 120000,
      supportsTypedFinalize: true
    })

    capturedId = initReq.id

    // Send the initialize response
    fromServer.push(
      JSON.stringify({
        jsonrpc: '2.0',
        id: capturedId,
        result: {
          serverInfo: { name: 'dotcraft', version: '0.2.0' },
          capabilities: { threadManagement: true }
        }
      }) + '\n'
    )

    const result = await initPromise

    // After response, the initialized notification should have been sent
    await new Promise((r) => setTimeout(r, 10))
    const initializedNotif = JSON.parse(lines[lines.length - 1])
    expect(initializedNotif.method).toBe('initialized')
    expect(initializedNotif.id).toBeUndefined()

    expect(result.serverInfo.name).toBe('dotcraft')
  })

  it('parses dashboardUrl from initialize result when present', async () => {
    const lines: string[] = []
    toServer.on('data', (chunk: Buffer) => {
      chunk
        .toString('utf8')
        .split('\n')
        .filter(Boolean)
        .forEach((line) => lines.push(line))
    })

    const initPromise = client.initialize()

    await new Promise((r) => setTimeout(r, 10))

    const initReq = JSON.parse(lines[0])
    const capturedId = initReq.id as number

    fromServer.push(
      JSON.stringify({
        jsonrpc: '2.0',
        id: capturedId,
        result: {
          serverInfo: { name: 'dotcraft', version: '0.2.0' },
          capabilities: { threadManagement: true },
          dashboardUrl: 'http://127.0.0.1:8080/dashboard'
        }
      }) + '\n'
    )

    const result = await initPromise
    expect(result.dashboardUrl).toBe('http://127.0.0.1:8080/dashboard')
  })

  // ─── Ignored / malformed lines ───────────────────────────────────────────────

  it('ignores malformed JSON lines without throwing', async () => {
    const received: string[] = []
    client.onNotification((method) => received.push(method))

    fromServer.push('not-json\n')
    fromServer.push('\n')
    fromServer.push('{"jsonrpc":"2.0","method":"valid/event","params":{}}\n')

    await new Promise((r) => setTimeout(r, 10))

    expect(received).toEqual(['valid/event'])
  })
})

class MockReconnectTransport {
  private lineHandlers: Array<(line: string) => void> = []
  private closeHandlers: Array<() => void> = []
  private openHandlers: Array<() => void> = []
  /** When false, writeLine queues into pending (simulates disconnected WebSocket). */
  private connected = false
  private pending: string[] = []
  readonly writes: string[] = []

  onLine(handler: (line: string) => void): () => void {
    this.lineHandlers.push(handler)
    return () => {
      this.lineHandlers = this.lineHandlers.filter((h) => h !== handler)
    }
  }

  onClose(handler: () => void): () => void {
    this.closeHandlers.push(handler)
    return () => {
      this.closeHandlers = this.closeHandlers.filter((h) => h !== handler)
    }
  }

  onOpen(handler: () => void): () => void {
    this.openHandlers.push(handler)
    return () => {
      this.openHandlers = this.openHandlers.filter((h) => h !== handler)
    }
  }

  async writeLine(line: string): Promise<void> {
    if (this.connected) {
      this.writes.push(line)
    } else {
      this.pending.push(line)
    }
  }

  flushPendingWrites(): void {
    for (const line of this.pending) {
      this.writes.push(line)
    }
    this.pending = []
  }

  dispose(): void {
    this.close()
  }

  open(): void {
    this.connected = true
    for (const h of this.openHandlers) h()
  }

  close(): void {
    this.connected = false
    this.pending = []
    for (const h of this.closeHandlers) h()
  }

  serverSend(message: unknown): void {
    const line = JSON.stringify(message)
    for (const h of this.lineHandlers) h(line)
  }
}

describe('WireProtocolClient websocket reconnect', () => {
  it('re-initializes automatically when websocket reconnects', async () => {
    const transport = new MockReconnectTransport()
    const client = new WireProtocolClient(
      transport as unknown as Readable,
      undefined,
      { autoInitializeOnTransportOpen: true }
    )
    const ready = vi.fn()
    const reconnected = vi.fn()
    client.on('ready', ready)
    client.on('reconnected', reconnected)

    transport.open()
    await new Promise((r) => setTimeout(r, 10))

    const init1 = JSON.parse(transport.writes[0] ?? '{}')
    expect(init1.method).toBe('initialize')
    transport.serverSend({
      jsonrpc: '2.0',
      id: init1.id,
      result: {
        serverInfo: { name: 'dotcraft', version: '0.2.0' },
        capabilities: { threadManagement: true }
      }
    })
    await new Promise((r) => setTimeout(r, 10))
    expect(ready).toHaveBeenCalledTimes(1)

    transport.close()
    transport.open()
    await new Promise((r) => setTimeout(r, 10))

    const initLines = transport.writes
      .map((line) => JSON.parse(line))
      .filter((line) => line.method === 'initialize')
    const init2 = initLines[1]
    expect(init2).toBeTruthy()
    transport.serverSend({
      jsonrpc: '2.0',
      id: init2.id,
      result: {
        serverInfo: { name: 'dotcraft', version: '0.2.1' },
        capabilities: { threadManagement: true }
      }
    })
    await new Promise((r) => setTimeout(r, 10))

    expect(reconnected).toHaveBeenCalledTimes(1)
    client.dispose()
  })

  it('flushes queued writes only after initialize handshake on reconnect', async () => {
    const transport = new MockReconnectTransport()
    const client = new WireProtocolClient(
      transport as unknown as Readable,
      undefined,
      { autoInitializeOnTransportOpen: true }
    )

    transport.open()
    await new Promise((r) => setTimeout(r, 10))
    const init1 = JSON.parse(transport.writes[0] ?? '{}')
    expect(init1.method).toBe('initialize')
    transport.serverSend({
      jsonrpc: '2.0',
      id: init1.id,
      result: {
        serverInfo: { name: 'dotcraft', version: '0.2.0' },
        capabilities: { threadManagement: true }
      }
    })
    await new Promise((r) => setTimeout(r, 10))

    transport.close()
    void client.sendRequest('thread/ping', { x: 1 }, 100).catch(() => {})

    transport.open()
    await new Promise((r) => setTimeout(r, 10))

    const linesBeforeResponse = transport.writes.map((l) => JSON.parse(l))
    let initSeen = 0
    const secondInitIdx = linesBeforeResponse.findIndex((l) => {
      if (l.method === 'initialize') {
        initSeen++
        return initSeen === 2
      }
      return false
    })
    expect(secondInitIdx).toBeGreaterThanOrEqual(0)

    const init2 = linesBeforeResponse[secondInitIdx] as { id?: number }
    transport.serverSend({
      jsonrpc: '2.0',
      id: init2.id,
      result: {
        serverInfo: { name: 'dotcraft', version: '0.2.1' },
        capabilities: { threadManagement: true }
      }
    })
    await new Promise((r) => setTimeout(r, 10))

    const linesAfter = transport.writes.map((l) => JSON.parse(l))
    const pingIdx = linesAfter.findIndex((l) => l.method === 'thread/ping')
    expect(pingIdx).toBeGreaterThanOrEqual(0)
    expect(secondInitIdx).toBeLessThan(pingIdx)

    client.dispose()
  })

  it('handles desktop-starts-first by waiting for transport open before initialize', async () => {
    const transport = new MockReconnectTransport()
    const client = new WireProtocolClient(
      transport as unknown as Readable,
      undefined,
      { autoInitializeOnTransportOpen: true }
    )

    expect(transport.writes).toHaveLength(0)
    const readyPromise = new Promise<void>((resolve) => {
      client.once('ready', () => resolve())
    })

    transport.open()
    await new Promise((r) => setTimeout(r, 10))
    const init = JSON.parse(transport.writes[0] ?? '{}')
    expect(init.method).toBe('initialize')
    transport.serverSend({
      jsonrpc: '2.0',
      id: init.id,
      result: {
        serverInfo: { name: 'dotcraft', version: '0.2.0' },
        capabilities: {}
      }
    })

    await readyPromise
    client.dispose()
  })
})

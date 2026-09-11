import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import {
  FRAME_ACK_TIMEOUT_MS,
  SCREEN_FRAME_HEADER_BYTES,
  type ScreenFrame,
  type ScreenViewStatus
} from '../../shared/screenView'
import type { ScreenSocket, ScreenSocketHandlers, ScreenSocketTarget } from '../screenView/screenSocket'
import { ScreenViewSession } from '../screenView/screenViewSession'

interface FakeSocket extends ScreenSocket {
  sent: string[]
  closed: boolean
  handlers: ScreenSocketHandlers
}

function frameBytes(sequence: number, jpeg: number[] = [1, 2, 3]): Uint8Array {
  const bytes = new Uint8Array(SCREEN_FRAME_HEADER_BYTES + jpeg.length)
  const view = new DataView(bytes.buffer)
  view.setUint32(0, sequence, true)
  view.setUint16(4, 640, true)
  view.setUint16(6, 360, true)
  view.setBigInt64(8, 0n, true)
  bytes.set(jpeg, SCREEN_FRAME_HEADER_BYTES)
  return bytes
}

function harness(options: { resolveBridge?: () => Promise<ScreenSocketTarget> } = {}): {
  sockets: FakeSocket[]
  states: ScreenViewStatus[]
  frames: ScreenFrame[]
  sessionIds: string[]
  session: ScreenViewSession
} {
  const sockets: FakeSocket[] = []
  const states: ScreenViewStatus[] = []
  const frames: ScreenFrame[] = []
  const sessionIds: string[] = []
  let nextSessionId = 0

  const session = new ScreenViewSession({
    peerId: 'peer-1',
    maxWidth: 640,
    watchers: 1,
    resolveBridge:
      options.resolveBridge ??
      (async () => ({ url: 'ws://hub/v1/satellites/peer-1/bridge', headers: {} })),
    createSocket: (_target, handlers) => {
      const socket: FakeSocket = {
        sent: [],
        closed: false,
        handlers,
        sendText(text) {
          socket.sent.push(text)
        },
        close() {
          socket.closed = true
        }
      }
      sockets.push(socket)
      return socket
    },
    newSessionId: () => {
      const id = `session-${++nextSessionId}`
      sessionIds.push(id)
      return id
    },
    onState: (status) => states.push(status),
    onFrame: (frame) => frames.push(frame)
  })

  return { sockets, states, frames, sessionIds, session }
}

async function settle(): Promise<void> {
  await Promise.resolve()
  await Promise.resolve()
}

describe('ScreenViewSession', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('sends the demand as a bare control record when the socket opens', async () => {
    const { session, sockets } = harness()
    session.start()
    await settle()
    sockets[0].handlers.onOpen()

    expect(JSON.parse(sockets[0].sent[0])).toEqual({
      watchers: 1,
      fps: 8,
      maxWidth: 640,
      quality: 82
    })
  })

  it('retunes the requested width without reconnecting, and only when it changes', async () => {
    const { session, sockets } = harness()
    session.start()
    await settle()
    sockets[0].handlers.onOpen()
    session.setMaxWidth(1920)

    expect(sockets).toHaveLength(1)
    expect(JSON.parse(sockets[0].sent[1]).maxWidth).toBe(1920)

    session.setMaxWidth(1920)
    expect(sockets[0].sent).toHaveLength(2)
  })

  it('pauses capture with watchers zero and closes on close', async () => {
    const { session, sockets } = harness()
    session.start()
    await settle()
    sockets[0].handlers.onOpen()
    session.setWatchers(0)
    expect(JSON.parse(sockets[0].sent[1]).watchers).toBe(0)

    session.setWatchers(1)
    session.close()
    expect(JSON.parse(sockets[0].sent[sockets[0].sent.length - 1]).watchers).toBe(0)
    expect(sockets[0].closed).toBe(true)
  })

  it('keeps one undelivered frame and hands over the newest after the ack', async () => {
    const { session, sockets, frames } = harness()
    session.start()
    await settle()
    sockets[0].handlers.onOpen()

    sockets[0].handlers.onBinary(frameBytes(1))
    sockets[0].handlers.onBinary(frameBytes(2))
    sockets[0].handlers.onBinary(frameBytes(3))
    expect(frames.map((frame) => frame.sequence)).toEqual([1])

    session.ack()
    expect(frames.map((frame) => frame.sequence)).toEqual([1, 3])
  })

  it('resets the credit when the renderer never acks', async () => {
    const { session, sockets, frames } = harness()
    session.start()
    await settle()
    sockets[0].handlers.onOpen()

    sockets[0].handlers.onBinary(frameBytes(1))
    sockets[0].handlers.onBinary(frameBytes(2))
    expect(frames).toHaveLength(1)

    vi.advanceTimersByTime(FRAME_ACK_TIMEOUT_MS)
    expect(frames.map((frame) => frame.sequence)).toEqual([1, 2])
  })

  it('maps close reasons to view states', async () => {
    const paused = harness()
    paused.session.start()
    await settle()
    paused.sockets[0].handlers.onOpen()
    paused.sockets[0].handlers.onClose(1000, 'sharingPaused')
    expect(paused.states.at(-1)).toEqual({ kind: 'paused' })

    const authorization = harness()
    authorization.session.start()
    await settle()
    authorization.sockets[0].handlers.onOpen()
    authorization.sockets[0].handlers.onClose(1000, 'authorizationRequired')
    expect(authorization.states.at(-1)).toEqual({ kind: 'paused', needsAuthorization: true })

    for (const reason of ['hostClosed', 'satelliteOffline', 'satelliteSessionFailed', 'peerClosed', '']) {
      const view = harness()
      view.session.start()
      await settle()
      view.sockets[0].handlers.onOpen()
      view.sockets[0].handlers.onClose(1006, reason)
      expect(view.states.at(-1)).toEqual({ kind: 'reconnecting' })
      view.session.close()
    }
  })

  it('reports an unknown peer as offline and a refused dial as unavailable', async () => {
    const offline = harness()
    offline.session.start()
    await settle()
    offline.sockets[0].handlers.onHandshakeStatus(404)
    expect(offline.states.at(-1)).toEqual({ kind: 'offline' })
    offline.session.close()

    const rejected = harness()
    rejected.session.start()
    await settle()
    rejected.sockets[0].handlers.onHandshakeStatus(401)
    expect(rejected.states.at(-1)).toEqual({ kind: 'unavailable', reason: 'hubRejected' })
    vi.advanceTimersByTime(60_000)
    expect(rejected.sockets).toHaveLength(1)
  })

  it('redials an enrolled but offline machine every 10 s, without giving up', async () => {
    const { session, sockets, states } = harness()
    session.start()
    await settle()

    for (let attempt = 0; attempt < 4; attempt++) {
      sockets[sockets.length - 1].handlers.onHandshakeStatus(503)
      expect(states.at(-1)).toEqual({ kind: 'offline' })
      const before = sockets.length
      vi.advanceTimersByTime(9000)
      await settle()
      expect(sockets).toHaveLength(before)
      vi.advanceTimersByTime(1000)
      await settle()
      expect(sockets).toHaveLength(before + 1)
    }
    session.close()
  })

  it('carries the host detail into the unavailable state', async () => {
    const { session, sockets, states } = harness()
    session.start()
    await settle()
    sockets[0].handlers.onOpen()
    sockets[0].handlers.onText(
      '{"enabled":true,"unavailableReason":"captureFailed","detail":"BitBlt failed (Win32 6)"}'
    )

    expect(states.at(-1)).toEqual({
      kind: 'unavailable',
      reason: 'captureFailed',
      detail: 'BitBlt failed (Win32 6)'
    })
    session.close()
  })

  it('retries a 409 once with a fresh session id', async () => {
    const { session, sockets, sessionIds } = harness()
    session.start()
    await settle()
    sockets[0].handlers.onHandshakeStatus(409)
    await settle()

    expect(sockets).toHaveLength(2)
    expect(sessionIds[1]).not.toBe(sessionIds[0])

    sockets[1].handlers.onHandshakeStatus(409)
    expect(sessionIds).toHaveLength(2)
    session.close()
  })

  it('stops reconnecting on a terminal capability and keeps the socket on a transient one', async () => {
    const terminal = harness()
    terminal.session.start()
    await settle()
    terminal.sockets[0].handlers.onOpen()
    terminal.sockets[0].handlers.onText('{"enabled":true,"unavailableReason":"noDisplayServer"}')

    expect(terminal.states.at(-1)).toEqual({ kind: 'unavailable', reason: 'noDisplayServer' })
    expect(terminal.sockets[0].closed).toBe(true)
    vi.advanceTimersByTime(60_000)
    await settle()
    expect(terminal.sockets).toHaveLength(1)

    const transient = harness()
    transient.session.start()
    await settle()
    transient.sockets[0].handlers.onOpen()
    transient.sockets[0].handlers.onText('{"enabled":true,"unavailableReason":"noInteractiveSession"}')
    expect(transient.states.at(-1)).toEqual({ kind: 'unavailable', reason: 'noInteractiveSession' })
    expect(transient.sockets[0].closed).toBe(false)

    transient.sockets[0].handlers.onText('{"enabled":true}')
    expect(transient.states.at(-1)).toEqual({ kind: 'connecting' })
    transient.session.close()
  })

  it('backs off to a ceiling and resets the wait once a dial opens', async () => {
    const { session, sockets } = harness()
    session.start()
    await settle()

    const waits: number[] = []
    for (let attempt = 0; attempt < 5; attempt++) {
      const socket = sockets[sockets.length - 1]
      const before = sockets.length
      socket.handlers.onClose(1006, 'hostClosed')
      const wait = await waitForNextDial(sockets, before)
      waits.push(wait)
    }
    expect(waits).toEqual([1000, 2000, 4000, 8000, 8000])

    sockets[sockets.length - 1].handlers.onOpen()
    const before = sockets.length
    sockets[sockets.length - 1].handlers.onClose(1006, 'hostClosed')
    expect(await waitForNextDial(sockets, before)).toBe(1000)
    session.close()
  })
})

async function waitForNextDial(sockets: FakeSocket[], before: number): Promise<number> {
  for (let elapsed = 1000; elapsed <= 16_000; elapsed += 1000) {
    vi.advanceTimersByTime(1000)
    await Promise.resolve()
    await Promise.resolve()
    if (sockets.length > before) return elapsed
  }
  throw new Error('the session never dialed again')
}

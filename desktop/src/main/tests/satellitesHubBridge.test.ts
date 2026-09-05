import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('electron', () => ({
  BrowserWindow: { getAllWindows: () => [] }
}))

import type { HubEvent } from '@dotcraft/sdk/hub'
import type { SatelliteEvent } from '../../shared/satellites'
import { SatellitesHubBridge } from '../satellites/satellitesHubBridge'
import type { DesktopHubClient } from '../desktopHub'

interface FakeHub {
  client: DesktopHubClient
  emit: (event: HubEvent) => void
  endStream: (error?: Error) => void
  subscribeCalls: number
  satellites: unknown[]
  listCalls: number
}

function fakeHub(): FakeHub {
  const state = {
    subscribeCalls: 0,
    listCalls: 0,
    satellites: [] as unknown[],
    onEvent: null as ((event: HubEvent) => void) | null,
    settle: null as ((error?: Error) => void) | null
  }

  const client = {
    subscribeEvents(onEvent: (event: HubEvent) => void, signal: AbortSignal): Promise<void> {
      state.subscribeCalls += 1
      state.onEvent = onEvent
      return new Promise<void>((resolve, reject) => {
        state.settle = (error?: Error) => (error ? reject(error) : resolve())
        signal.addEventListener('abort', () => resolve())
      })
    },
    listSatellites(): Promise<unknown[]> {
      state.listCalls += 1
      return Promise.resolve(state.satellites)
    }
  } as unknown as DesktopHubClient

  return {
    client,
    emit: (event) => state.onEvent?.(event),
    endStream: (error) => state.settle?.(error),
    get subscribeCalls() {
      return state.subscribeCalls
    },
    get listCalls() {
      return state.listCalls
    },
    get satellites() {
      return state.satellites
    },
    set satellites(value: unknown[]) {
      state.satellites = value
    }
  }
}

function satelliteFrame(kind: string, peerId: string): HubEvent {
  return { kind, at: '2026-09-05T12:00:00.000Z', data: { peerId } }
}

let hub: FakeHub
let received: SatelliteEvent[]
let bridge: SatellitesHubBridge

beforeEach(() => {
  vi.useFakeTimers()
  hub = fakeHub()
  received = []
  bridge = new SatellitesHubBridge({
    getHubClient: () => hub.client,
    broadcast: (event) => received.push(event)
  })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('SatellitesHubBridge subscription', () => {
  it('opens one Hub stream no matter how many windows subscribe', () => {
    bridge.acquire()
    bridge.acquire()
    bridge.acquire()

    expect(hub.subscribeCalls).toBe(1)
  })

  it('keeps the stream while any subscriber remains and closes it at zero', async () => {
    bridge.acquire()
    bridge.acquire()

    bridge.release()
    hub.emit(satelliteFrame('satellite.online', 'sat_1'))
    await vi.advanceTimersByTimeAsync(0)
    expect(received).toHaveLength(1)

    bridge.release()
    await vi.advanceTimersByTimeAsync(0)

    // The abort resolves the stream; with no subscribers nothing resubscribes.
    await vi.advanceTimersByTimeAsync(60_000)
    expect(hub.subscribeCalls).toBe(1)
  })

  it('falls back to polling presence when the stream drops', async () => {
    hub.satellites = [{ peerId: 'sat_1', displayName: 'Ann PC', online: true }]
    bridge.acquire()
    hub.endStream(new Error('hub unavailable'))
    await vi.advanceTimersByTimeAsync(0)

    // The first tick only records the baseline; it must not announce arrivals.
    await vi.advanceTimersByTimeAsync(30_000)
    expect(hub.listCalls).toBe(1)
    expect(received).toHaveLength(0)

    hub.satellites = [{ peerId: 'sat_1', displayName: 'Ann PC', online: false }]
    hub.endStream(new Error('still unavailable'))
    await vi.advanceTimersByTimeAsync(30_000)

    expect(received.map((event) => [event.kind, event.peerId])).toEqual([['offline', 'sat_1']])
  })
})

describe('SatellitesHubBridge event filtering', () => {
  it('ignores Hub events that are not satellite enrollment', async () => {
    bridge.acquire()

    hub.emit({ kind: 'appserver.started', at: '2026-09-05T12:00:00.000Z', workspacePath: 'C:/ws' })
    hub.emit({ kind: 'notification.requested', at: '2026-09-05T12:00:00.000Z', data: { title: 'x' } })
    hub.emit(satelliteFrame('satellite.unknown', 'sat_1'))
    await vi.advanceTimersByTimeAsync(0)

    expect(received).toHaveLength(0)
  })

  it('attaches the machine record it already knows', async () => {
    bridge.acquire()
    bridge.remember([
      {
        peerId: 'sat_1',
        hostId: 'sat_1',
        displayName: 'Ann PC',
        connected: true,
        workspaces: []
      }
    ])

    hub.emit(satelliteFrame('satellite.offline', 'sat_1'))
    await vi.advanceTimersByTimeAsync(0)

    expect(received[0].satellite?.displayName).toBe('Ann PC')
    expect(hub.listCalls).toBe(0)
  })

  it('looks a newly joined machine up once so it can be named', async () => {
    hub.satellites = [{ peerId: 'sat_2', displayName: 'Bo PC', online: true }]
    bridge.acquire()

    hub.emit(satelliteFrame('satellite.joined', 'sat_2'))
    await vi.advanceTimersByTimeAsync(0)

    expect(hub.listCalls).toBe(1)
    expect(received[0]).toMatchObject({ kind: 'joined', peerId: 'sat_2' })
    expect(received[0].satellite?.displayName).toBe('Bo PC')
  })
})

describe('SatellitesHubBridge recent activity', () => {
  it('keeps at most twenty events per machine, newest first', async () => {
    bridge.acquire()
    for (let i = 0; i < 25; i++) {
      hub.emit(satelliteFrame(i % 2 === 0 ? 'satellite.online' : 'satellite.offline', 'sat_1'))
    }
    await vi.advanceTimersByTimeAsync(0)

    const activity = bridge.recentActivity('sat_1')
    expect(activity).toHaveLength(20)
    // Event 24 was the 25th emitted and used the even (online) kind.
    expect(activity[0].kind).toBe('online')
  })

  it('keeps at most two hundred events across machines', async () => {
    bridge.acquire()
    for (let peer = 0; peer < 15; peer++) {
      for (let i = 0; i < 20; i++) {
        hub.emit(satelliteFrame('satellite.online', `sat_${peer}`))
      }
    }
    await vi.advanceTimersByTimeAsync(0)

    expect(bridge.recentActivity()).toHaveLength(200)
    expect(bridge.recentActivity('sat_0')).toHaveLength(0)
    expect(bridge.recentActivity('sat_14')).toHaveLength(20)
  })
})

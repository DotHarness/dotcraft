import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('electron', () => ({
  BrowserWindow: { getAllWindows: () => [] }
}))

import type { HubEvent, HubMobileState } from '@dotcraft/sdk/hub'
import type { MobileEvent } from '../../shared/mobile'
import type { DesktopHubClient } from '../desktopHub'
import { MobileHubBridge } from '../mobile/mobileHubBridge'

const HUB_STATE: HubMobileState = {
  state: 'on',
  port: 47610,
  addresses: ['192.168.1.20'],
  devices: [
    {
      deviceId: 'dev_1',
      displayName: 'iPhone 16',
      platform: 'ios',
      osVersion: '18.6',
      appVersion: '0.8.1',
      pairedAt: '2026-10-03T08:00:00Z',
      lastSeenAt: null,
      connected: true
    }
  ]
}

const STATUS = {
  state: 'on',
  port: 47610,
  devices: [
    {
      deviceId: 'dev_1',
      displayName: 'iPhone 16',
      platform: 'ios',
      osVersion: '18.6',
      pairedAt: '2026-10-03T08:00:00Z',
      lastSeenAt: null,
      connected: true
    }
  ]
}

let onHubEvent: (event: HubEvent) => void
let failStream: (error: Error) => void
let hub: { subscribeEvents: ReturnType<typeof vi.fn>; getMobile: ReturnType<typeof vi.fn> }
let received: MobileEvent[]
let bridge: MobileHubBridge

beforeEach(() => {
  vi.useFakeTimers()
  hub = {
    subscribeEvents: vi.fn((listener: (event: HubEvent) => void, signal: AbortSignal) => {
      onHubEvent = listener
      return new Promise<void>((resolve, reject) => {
        failStream = reject
        signal.addEventListener('abort', () => resolve())
      })
    }),
    getMobile: vi.fn(async () => HUB_STATE)
  }
  received = []
  bridge = new MobileHubBridge({
    getHubClient: () => hub as unknown as DesktopHubClient,
    broadcast: (event) => received.push(event)
  })
})

afterEach(() => {
  vi.useRealTimers()
})

describe('MobileHubBridge', () => {
  it('shares one Hub stream across windows and closes it with the last one', async () => {
    bridge.acquire()
    bridge.acquire()
    bridge.release()
    bridge.release()
    await vi.advanceTimersByTimeAsync(60_000)

    expect(hub.subscribeEvents).toHaveBeenCalledTimes(1)
    expect(hub.getMobile).not.toHaveBeenCalled()
  })

  it('polls the state while the stream is down and then subscribes again', async () => {
    bridge.acquire()
    failStream(new Error('hub unavailable'))
    await vi.advanceTimersByTimeAsync(30_000)

    expect(hub.getMobile).toHaveBeenCalledTimes(1)
    expect(hub.subscribeEvents).toHaveBeenCalledTimes(2)
    expect(received).toEqual([{ kind: 'stateChanged', status: STATUS }])
  })

  it('forwards phone events and ignores the rest of the Hub stream', () => {
    bridge.acquire()

    onHubEvent({ kind: 'satellite.online', at: '2026-10-03T08:00:00Z', data: { peerId: 'sat_1' } })
    onHubEvent({ kind: 'mobile.stateChanged', at: '2026-10-03T08:00:00Z', data: HUB_STATE })
    onHubEvent({
      kind: 'mobile.devicePaired',
      at: '2026-10-03T08:00:00Z',
      data: { deviceId: 'dev_2', displayName: 'Pixel 9', platform: 'android', pairingId: 'pair_1' }
    })
    onHubEvent({ kind: 'mobile.deviceRevoked', at: '2026-10-03T08:00:00Z', data: { deviceId: 'dev_1' } })
    onHubEvent({
      kind: 'mobile.deviceSeen',
      at: '2026-10-03T08:00:00Z',
      data: { deviceId: 'dev_2', lastSeenAt: '2026-10-03T08:00:00Z', connected: true }
    })

    expect(received).toEqual([
      { kind: 'stateChanged', status: STATUS },
      { kind: 'devicePaired', deviceId: 'dev_2', displayName: 'Pixel 9', pairingId: 'pair_1' },
      { kind: 'deviceRevoked', deviceId: 'dev_1' },
      { kind: 'deviceSeen', deviceId: 'dev_2', lastSeenAt: '2026-10-03T08:00:00Z', connected: true }
    ])
  })
})

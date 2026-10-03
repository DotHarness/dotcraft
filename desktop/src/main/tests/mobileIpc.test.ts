import { beforeEach, describe, expect, it, vi } from 'vitest'

const ipc = vi.hoisted(() => ({
  listeners: new Map<string, (event: unknown) => void>()
}))

vi.mock('electron', () => ({
  ipcMain: {
    on: vi.fn((channel: string, listener: (event: unknown) => void) => ipc.listeners.set(channel, listener)),
    removeAllListeners: vi.fn()
  }
}))

import type { MobileStatusResult } from '../../shared/mobile'
import type { DesktopHubClient } from '../desktopHub'
import type { MobileHubBridge } from '../mobile/mobileHubBridge'
import { registerMobileHandlers } from '../mobile/mobileIpc'

const HUB_TOKEN = 'hub-bearer-token-never-crosses'

const STATE = {
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
      connected: true,
      token: HUB_TOKEN
    }
  ],
  token: HUB_TOKEN
}

const PAIRING = { pairingId: 'pair_1', qrPayload: 'dotcraft://pair?v=1&code=abc', expiresAt: '2026-10-03T08:10:00Z' }

function hubError(code: string, message: string): Error {
  return Object.assign(new Error(message), { code })
}

function harness() {
  const handlers = new Map<string, (event: unknown, ...args: unknown[]) => unknown>()
  const hub = {
    getStatus: vi.fn(async () => ({ capabilities: { mobile: true } })),
    getMobile: vi.fn(async () => STATE),
    enableMobile: vi.fn(async () => STATE),
    disableMobile: vi.fn(async () => ({ ...STATE, state: 'off' })),
    createMobilePairing: vi.fn(async () => PAIRING),
    revokeMobileDevice: vi.fn(async () => undefined),
    setMobileRelay: vi.fn(async (url: string, token: string) => ({
      ...STATE,
      relay: { url, state: 'connecting', token }
    })),
    clearMobileRelay: vi.fn(async () => ({ ...STATE, relay: null }))
  }
  const bridge = { acquire: vi.fn(), release: vi.fn() }

  registerMobileHandlers({
    handleSafe: (channel, listener) => handlers.set(channel, listener as never),
    getHubClient: () => hub as unknown as DesktopHubClient,
    bridge: bridge as unknown as MobileHubBridge
  })

  return {
    hub,
    bridge,
    invoke: async (channel: string, input?: unknown): Promise<unknown> => handlers.get(channel)!({}, input)
  }
}

beforeEach(() => {
  ipc.listeners.clear()
})

describe('mobile:status', () => {
  it('returns the reduced state and never the Hub token', async () => {
    const app = harness()
    const result = (await app.invoke('mobile:status')) as MobileStatusResult

    expect(result).toEqual({
      supported: true,
      status: {
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
    })
    expect(JSON.stringify(result)).not.toContain(HUB_TOKEN)
  })

  it('keeps a Hub without phone access in the setup state', async () => {
    const app = harness()
    app.hub.getStatus.mockResolvedValue({ capabilities: {} } as never)
    app.hub.getMobile.mockRejectedValue(hubError('hubRequestFailed', 'Hub request failed with HTTP 404.'))

    expect(await app.invoke('mobile:status')).toEqual({ supported: false, status: null })
  })

  it('reports a failed read from a Hub that has phone access', async () => {
    const app = harness()
    app.hub.getMobile.mockRejectedValue(hubError('hubInternalError', 'Gateway state unavailable.'))

    expect(await app.invoke('mobile:status')).toEqual({
      supported: true,
      status: null,
      error: 'Gateway state unavailable.'
    })
  })
})

describe('mobile requests', () => {
  it('maps each request to its Hub call', async () => {
    const app = harness()

    expect(await app.invoke('mobile:enable')).toMatchObject({ ok: true, value: { state: 'on' } })
    expect(await app.invoke('mobile:disable')).toMatchObject({ ok: true, value: { state: 'off' } })
    expect(await app.invoke('mobile:create-pairing')).toEqual({ ok: true, value: PAIRING })
    expect(await app.invoke('mobile:revoke', { deviceId: 'dev_1' })).toEqual({ ok: true, value: undefined })
    expect(app.hub.revokeMobileDevice).toHaveBeenCalledWith('dev_1')
  })

  it('sends the relay token to Hub and never returns it', async () => {
    const app = harness()
    const relayToken = 'relay-token-stays-in-main'

    const result = await app.invoke('mobile:set-relay', { url: 'wss://relay.example.com', token: relayToken })

    expect(app.hub.setMobileRelay).toHaveBeenCalledWith('wss://relay.example.com', relayToken)
    expect(result).toMatchObject({ ok: true, value: { relay: { url: 'wss://relay.example.com', state: 'connecting' } } })
    expect(JSON.stringify(result)).not.toContain(relayToken)
  })

  it('clears the relay', async () => {
    const app = harness()

    expect(await app.invoke('mobile:clear-relay')).toMatchObject({ ok: true, value: { relay: null } })
    expect(app.hub.clearMobileRelay).toHaveBeenCalledTimes(1)
  })

  it('returns the Hub error code so the renderer can explain a failure', async () => {
    const app = harness()
    app.hub.enableMobile.mockRejectedValue(hubError('portUnavailable', 'Port 47610 is in use.'))

    expect(await app.invoke('mobile:enable')).toEqual({
      ok: false,
      failure: { code: 'portUnavailable', message: 'Port 47610 is in use.' }
    })
  })
})

describe('mobile event subscription', () => {
  function sender(id: number) {
    const destroyed: Array<() => void> = []
    return {
      event: { sender: { id, once: (_name: string, listener: () => void) => destroyed.push(listener) } },
      destroy: () => destroyed.forEach((listener) => listener())
    }
  }

  it('holds the Hub stream once per window and releases it on unsubscribe or close', () => {
    const app = harness()
    const subscribe = ipc.listeners.get('mobile:subscribe')!
    const unsubscribe = ipc.listeners.get('mobile:unsubscribe')!
    const first = sender(101)
    const second = sender(102)

    subscribe(first.event)
    subscribe(first.event)
    subscribe(second.event)
    expect(app.bridge.acquire).toHaveBeenCalledTimes(2)

    unsubscribe(first.event)
    second.destroy()
    unsubscribe(first.event)
    expect(app.bridge.release).toHaveBeenCalledTimes(2)
  })
})

import { beforeEach, describe, expect, it, vi } from 'vitest'
import { connectMobile, useMobileStore } from '../stores/mobileStore'
import type { MobileDevice, MobileEvent, MobilePairing, MobileStatus } from '../../shared/mobile'

const status = vi.fn()
const enable = vi.fn()
const disable = vi.fn()
const createPairing = vi.fn()
const allowPairing = vi.fn()
const denyPairing = vi.fn()
const revoke = vi.fn()
const setRelay = vi.fn()
const clearRelay = vi.fn()
const onEvent = vi.fn()

const IPHONE: MobileDevice = {
  deviceId: 'dev_1',
  displayName: 'iPhone 16',
  platform: 'ios',
  osVersion: '18.6',
  pairedAt: '2026-10-03T08:00:00Z',
  lastSeenAt: null,
  connected: true
}

const PIXEL: MobileDevice = {
  deviceId: 'dev_2',
  displayName: 'Pixel 9',
  platform: 'android',
  osVersion: '15',
  pairedAt: '2026-09-21T08:00:00Z',
  lastSeenAt: '2026-10-03T07:55:00Z',
  connected: false
}

const PAIRING: MobilePairing = {
  pairingId: 'pair_1',
  qrPayload: 'dotcraft://pair?v=1&code=abc',
  expiresAt: '2026-10-03T08:10:00Z'
}

function state(overrides: Partial<MobileStatus> = {}): MobileStatus {
  return { state: 'on', port: 47610, relay: null, devices: [IPHONE, PIXEL], ...overrides }
}

function apply(event: MobileEvent): void {
  useMobileStore.getState().applyEvent(event)
}

beforeEach(() => {
  vi.clearAllMocks()
  onEvent.mockReturnValue(() => undefined)
  status.mockResolvedValue({ supported: true, status: state() })
  Object.defineProperty(globalThis, 'window', { configurable: true, value: {} })
  Object.defineProperty(globalThis.window, 'api', {
    configurable: true,
    value: { mobile: { status, enable, disable, createPairing, allowPairing, denyPairing, revoke, setRelay, clearRelay, onEvent } }
  })
  useMobileStore.getState().closePairing()
  useMobileStore.setState({
    supported: true,
    loaded: false,
    error: null,
    gateway: null,
    devices: [],
    pendingEnabled: null
  })
})

describe('mobileStore loading', () => {
  it('splits the Hub state into the gateway and the phone list and keeps it when a later read fails', async () => {
    await useMobileStore.getState().load()
    const store = useMobileStore.getState()

    expect(store.loaded).toBe(true)
    expect(store.gateway).toEqual({ state: 'on', port: 47610, relay: null })
    expect(store.devices).toEqual([IPHONE, PIXEL])

    status.mockResolvedValue({ supported: true, status: null, error: 'hub down' })
    await useMobileStore.getState().load()

    expect(useMobileStore.getState().error).toBe('hub down')
    expect(useMobileStore.getState().devices).toHaveLength(2)
  })

  it('follows Hub events while connected and stops when released', () => {
    const release = vi.fn()
    onEvent.mockReturnValue(release)

    const disconnect = connectMobile()
    expect(status).toHaveBeenCalledTimes(1)
    expect(onEvent).toHaveBeenCalledTimes(1)

    disconnect()
    expect(release).toHaveBeenCalledTimes(1)
  })
})

describe('mobileStore gateway switch', () => {
  it('applies the state Hub returns', async () => {
    useMobileStore.setState({ gateway: { state: 'off', port: 47610, relay: null }, devices: [PIXEL] })
    enable.mockResolvedValue({ ok: true, value: state({ devices: [PIXEL] }) })

    const pending = useMobileStore.getState().setEnabled(true)
    expect(useMobileStore.getState().pendingEnabled).toBe(true)
    expect(await pending).toBeNull()

    expect(useMobileStore.getState()).toMatchObject({ pendingEnabled: null, gateway: { state: 'on' } })
  })

  it('shows a failed start with its reason, keeps the phone list, and leaves the gateway as it was when turning off fails', async () => {
    useMobileStore.setState({ gateway: { state: 'off', port: 47610, relay: null }, devices: [PIXEL] })
    enable.mockResolvedValue({ ok: false, failure: { code: 'portUnavailable', message: 'Port 47610 is in use.' } })

    const failure = await useMobileStore.getState().setEnabled(true)

    expect(failure?.code).toBe('portUnavailable')
    expect(useMobileStore.getState().gateway).toEqual({
      state: 'failed',
      port: 47610,
      relay: null,
      failureCode: 'portUnavailable',
      failureMessage: 'Port 47610 is in use.'
    })
    expect(useMobileStore.getState().devices).toEqual([PIXEL])

    useMobileStore.setState({ gateway: { state: 'on', port: 47610, relay: null } })
    disable.mockResolvedValue({ ok: false, failure: { code: 'hubInternalError', message: 'no' } })

    expect(await useMobileStore.getState().setEnabled(false)).toMatchObject({ code: 'hubInternalError' })
    expect(useMobileStore.getState()).toMatchObject({ pendingEnabled: null, gateway: { state: 'on' } })
  })
})

describe('mobileStore remove', () => {
  beforeEach(() => {
    useMobileStore.setState({ gateway: { state: 'off', port: 47610, relay: null }, devices: [IPHONE, PIXEL] })
  })

  it('drops the phone once Hub revokes it or no longer knows it, even with access off', async () => {
    revoke.mockResolvedValueOnce({ ok: true, value: undefined })

    expect(await useMobileStore.getState().revoke('dev_2')).toBeNull()
    expect(revoke).toHaveBeenCalledWith('dev_2')
    expect(useMobileStore.getState().devices).toEqual([IPHONE])

    revoke.mockResolvedValueOnce({ ok: false, failure: { code: 'deviceNotFound', message: 'gone' } })
    expect(await useMobileStore.getState().revoke('dev_1')).toBeNull()
    expect(useMobileStore.getState().devices).toEqual([])
  })

  it('keeps the phone when the revoke fails', async () => {
    revoke.mockResolvedValue({ ok: false, failure: { code: 'hubInternalError', message: 'no' } })

    expect(await useMobileStore.getState().revoke('dev_2')).toMatchObject({ code: 'hubInternalError' })
    expect(useMobileStore.getState().devices).toEqual([IPHONE, PIXEL])
  })
})

describe('mobileStore pairing', () => {
  it('shows the minted code, or keeps the failure so the dialog can offer a retry', async () => {
    createPairing.mockResolvedValueOnce({ ok: true, value: PAIRING })

    const minting = useMobileStore.getState().startPairing()
    expect(useMobileStore.getState().pairing).toEqual({ step: 'creating' })
    await minting

    expect(useMobileStore.getState().pairing).toEqual({ step: 'code', pairing: PAIRING })

    createPairing.mockResolvedValueOnce({ ok: false, failure: { code: 'gatewayOff', message: 'off' } })
    await useMobileStore.getState().startPairing()

    expect(useMobileStore.getState().pairing).toEqual({ step: 'error', failure: { code: 'gatewayOff', message: 'off' } })
  })

  it('drops the code on close and ignores a mint that finishes afterwards', async () => {
    let settle: (value: unknown) => void = () => undefined
    createPairing.mockReturnValue(new Promise((resolve) => (settle = resolve)))

    const minting = useMobileStore.getState().startPairing()
    useMobileStore.getState().closePairing()
    settle({ ok: true, value: PAIRING })
    await minting

    expect(useMobileStore.getState().pairing).toEqual({ step: 'idle' })
  })

  it('moves to paired only for the code this dialog minted and refreshes the list', async () => {
    createPairing.mockResolvedValue({ ok: true, value: PAIRING })
    await useMobileStore.getState().startPairing()

    apply({ kind: 'devicePaired', deviceId: 'dev_3', displayName: 'Other phone', pairingId: 'pair_other' })
    expect(useMobileStore.getState().pairing.step).toBe('code')

    apply({ kind: 'devicePaired', deviceId: 'dev_4', displayName: 'Galaxy S25', pairingId: 'pair_1' })
    expect(useMobileStore.getState().pairing).toEqual({ step: 'paired', displayName: 'Galaxy S25' })
    expect(status).toHaveBeenCalledTimes(2)
  })

  it('asks the computer about the phone that scanned its code, and shows a fresh code when declined or abandoned', async () => {
    const next: MobilePairing = { ...PAIRING, pairingId: 'pair_2' }
    createPairing.mockResolvedValueOnce({ ok: true, value: PAIRING }).mockResolvedValue({ ok: true, value: next })
    allowPairing.mockResolvedValue({ ok: true, value: undefined })
    denyPairing.mockResolvedValue({ ok: true, value: undefined })
    await useMobileStore.getState().startPairing()

    apply({ kind: 'pairingRequested', pairingId: 'pair_1', displayName: 'Galaxy S25' })
    await useMobileStore.getState().allowPairing()
    expect(allowPairing).toHaveBeenCalledWith('pair_1')
    expect(useMobileStore.getState().pairing).toMatchObject({ step: 'request', displayName: 'Galaxy S25', allowing: true })
    apply({ kind: 'devicePaired', deviceId: 'dev_4', displayName: 'Galaxy S25', pairingId: 'pair_1' })
    expect(useMobileStore.getState().pairing).toEqual({ step: 'paired', displayName: 'Galaxy S25' })

    createPairing.mockResolvedValueOnce({ ok: true, value: PAIRING })
    await useMobileStore.getState().startPairing()
    apply({ kind: 'pairingRequested', pairingId: 'pair_1', displayName: 'Stranger' })
    await useMobileStore.getState().denyPairing()
    expect(denyPairing).toHaveBeenCalledWith('pair_1')
    expect(useMobileStore.getState().pairing).toEqual({ step: 'code', pairing: next })

    apply({ kind: 'pairingRequested', pairingId: 'pair_2', displayName: 'Galaxy S25' })
    apply({ kind: 'pairingCancelled', pairingId: 'pair_2' })
    await vi.waitFor(() => expect(createPairing).toHaveBeenCalledTimes(4))
  })

  it('ends a waiting code when phone access turns off', async () => {
    createPairing.mockResolvedValue({ ok: true, value: PAIRING })
    await useMobileStore.getState().startPairing()

    apply({ kind: 'stateChanged', status: state({ state: 'off' }) })

    expect(useMobileStore.getState().pairing).toMatchObject({ step: 'error', failure: { code: 'gatewayOff' } })
  })
})

describe('mobileStore events', () => {
  beforeEach(() => {
    useMobileStore.setState({ gateway: { state: 'on', port: 47610, relay: null }, devices: [IPHONE, PIXEL] })
  })

  it('takes the gateway and the list a state change carries', () => {
    apply({ kind: 'stateChanged', status: state({ state: 'failed', failureCode: 'portUnavailable', devices: [PIXEL] }) })

    expect(useMobileStore.getState().gateway).toMatchObject({ state: 'failed', failureCode: 'portUnavailable' })
    expect(useMobileStore.getState().devices).toEqual([PIXEL])
  })

  it('updates presence from deviceSeen and removes a phone revoked elsewhere', () => {
    apply({ kind: 'deviceSeen', deviceId: 'dev_1', lastSeenAt: '2026-10-03T09:00:00Z', connected: false })

    expect(useMobileStore.getState().devices[0]).toMatchObject({
      deviceId: 'dev_1',
      connected: false,
      lastSeenAt: '2026-10-03T09:00:00Z'
    })

    apply({ kind: 'deviceRevoked', deviceId: 'dev_1' })
    expect(useMobileStore.getState().devices).toEqual([PIXEL])
  })
})

describe('mobileStore relay', () => {
  const RELAY_URL = 'wss://relay.example.com'

  beforeEach(() => {
    useMobileStore.setState({ gateway: { state: 'on', port: 47610, relay: null }, devices: [IPHONE, PIXEL] })
  })

  it('follows the relay through state changes', () => {
    apply({ kind: 'stateChanged', status: state({ relay: { url: RELAY_URL, state: 'connecting' } }) })
    expect(useMobileStore.getState().gateway?.relay).toEqual({ url: RELAY_URL, state: 'connecting' })

    apply({ kind: 'stateChanged', status: state({ relay: { url: RELAY_URL, state: 'connected' } }) })
    expect(useMobileStore.getState().gateway?.relay).toEqual({ url: RELAY_URL, state: 'connected' })

    apply({ kind: 'stateChanged', status: state() })
    expect(useMobileStore.getState().gateway?.relay).toBeNull()
    expect(useMobileStore.getState().devices).toEqual([IPHONE, PIXEL])
  })

  it('applies the state Hub returns for a new relay without keeping the token', async () => {
    setRelay.mockResolvedValue({ ok: true, value: state({ relay: { url: RELAY_URL, state: 'connecting' } }) })

    expect(await useMobileStore.getState().setRelay(RELAY_URL, 'relay-secret')).toBeNull()

    expect(setRelay).toHaveBeenCalledWith(RELAY_URL, 'relay-secret')
    expect(useMobileStore.getState().gateway?.relay).toEqual({ url: RELAY_URL, state: 'connecting' })
    expect(JSON.stringify(useMobileStore.getState())).not.toContain('relay-secret')
  })

  it('keeps the relay when removing it fails', async () => {
    useMobileStore.setState({ gateway: { state: 'on', port: 47610, relay: { url: RELAY_URL, state: 'connected' } } })
    clearRelay.mockResolvedValue({ ok: false, failure: { code: 'hubInternalError', message: 'no' } })

    expect(await useMobileStore.getState().clearRelay()).toMatchObject({ code: 'hubInternalError' })
    expect(useMobileStore.getState().gateway?.relay).toEqual({ url: RELAY_URL, state: 'connected' })
  })
})

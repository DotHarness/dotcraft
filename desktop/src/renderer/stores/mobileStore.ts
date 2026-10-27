import { create } from 'zustand'
import type {
  MobileDevice,
  MobileEvent,
  MobileFailure,
  MobileGateway,
  MobilePairing,
  MobileStatus
} from '../../shared/mobile'

export type PairingFlow =
  | { step: 'idle' }
  | { step: 'creating' }
  | { step: 'code'; pairing: MobilePairing }
  | { step: 'request'; pairing: MobilePairing; displayName: string; allowing: boolean }
  | { step: 'error'; failure: MobileFailure }
  | { step: 'paired'; displayName: string }

interface MobileState {
  supported: boolean
  loaded: boolean
  error: string | null
  gateway: MobileGateway | null
  devices: MobileDevice[]
  pendingEnabled: boolean | null
  pairing: PairingFlow
}

interface MobileStore extends MobileState {
  load(): Promise<void>
  setEnabled(enabled: boolean): Promise<MobileFailure | null>
  revoke(deviceId: string): Promise<MobileFailure | null>
  setRelay(url: string, token: string): Promise<MobileFailure | null>
  clearRelay(): Promise<MobileFailure | null>
  startPairing(): Promise<void>
  allowPairing(): Promise<void>
  denyPairing(): Promise<void>
  closePairing(): void
  applyEvent(event: MobileEvent): void
}

const IDLE: PairingFlow = { step: 'idle' }
const GATEWAY_OFF: PairingFlow = { step: 'error', failure: { code: 'gatewayOff', message: '' } }

let pairingRequest = 0

function pairingOf(flow: PairingFlow): MobilePairing | null {
  return flow.step === 'code' || flow.step === 'request' ? flow.pairing : null
}

function split(status: MobileStatus): { gateway: MobileGateway; devices: MobileDevice[] } {
  const { devices, ...gateway } = status
  return { gateway, devices }
}

export const useMobileStore = create<MobileStore>((set, get) => ({
  supported: true,
  loaded: false,
  error: null,
  gateway: null,
  devices: [],
  pendingEnabled: null,
  pairing: IDLE,

  async load() {
    try {
      const result = await window.api.mobile.status()
      set({
        supported: result.supported,
        error: result.error ?? null,
        loaded: true,
        ...(result.status ? split(result.status) : {})
      })
    } catch (error) {
      set({ loaded: true, error: error instanceof Error ? error.message : String(error) })
    }
  },

  async setEnabled(enabled) {
    set({ pendingEnabled: enabled })
    const result = await (enabled ? window.api.mobile.enable() : window.api.mobile.disable())
    if (result.ok) {
      set({ pendingEnabled: null, ...split(result.value) })
      return null
    }
    const { code, message } = result.failure
    set((state) => ({
      pendingEnabled: null,
      gateway:
        enabled && state.gateway
          ? { ...state.gateway, state: 'failed', failureCode: code, failureMessage: message }
          : state.gateway
    }))
    return result.failure
  },

  async revoke(deviceId) {
    const result = await window.api.mobile.revoke(deviceId)
    if (!result.ok && result.failure.code !== 'deviceNotFound') return result.failure
    set((state) => ({ devices: state.devices.filter((device) => device.deviceId !== deviceId) }))
    return null
  },

  async setRelay(url, token) {
    const result = await window.api.mobile.setRelay(url, token)
    if (!result.ok) return result.failure
    set(split(result.value))
    return null
  },

  async clearRelay() {
    const result = await window.api.mobile.clearRelay()
    if (!result.ok) return result.failure
    set(split(result.value))
    return null
  },

  async startPairing() {
    const request = ++pairingRequest
    set({ pairing: { step: 'creating' } })
    const result = await window.api.mobile.createPairing()
    if (request !== pairingRequest) return
    set({ pairing: result.ok ? { step: 'code', pairing: result.value } : { step: 'error', failure: result.failure } })
  },

  async allowPairing() {
    const { pairing } = get()
    if (pairing.step !== 'request' || pairing.allowing) return
    set({ pairing: { ...pairing, allowing: true } })
    const result = await window.api.mobile.allowPairing(pairing.pairing.pairingId)
    if (!result.ok && pairingOf(get().pairing)?.pairingId === pairing.pairing.pairingId) await get().startPairing()
  },

  async denyPairing() {
    const { pairing } = get()
    if (pairing.step !== 'request') return
    await window.api.mobile.denyPairing(pairing.pairing.pairingId)
    if (pairingOf(get().pairing)?.pairingId === pairing.pairing.pairingId) await get().startPairing()
  },

  closePairing() {
    pairingRequest += 1
    set({ pairing: IDLE })
  },

  applyEvent(event) {
    switch (event.kind) {
      case 'stateChanged': {
        const { pairing } = get()
        const stranded = event.status.state !== 'on' && (pairing.step === 'creating' || pairingOf(pairing) !== null)
        if (stranded) pairingRequest += 1
        set({ ...split(event.status), ...(stranded ? { pairing: GATEWAY_OFF } : {}) })
        return
      }
      case 'pairingRequested': {
        const { pairing } = get()
        if (pairing.step === 'code' && pairing.pairing.pairingId === event.pairingId) {
          set({ pairing: { step: 'request', pairing: pairing.pairing, displayName: event.displayName, allowing: false } })
        }
        return
      }
      case 'pairingCancelled':
        if (pairingOf(get().pairing)?.pairingId === event.pairingId) void get().startPairing()
        return
      case 'devicePaired': {
        const { pairing } = get()
        if (pairingOf(pairing)?.pairingId === event.pairingId) {
          set({ pairing: { step: 'paired', displayName: event.displayName } })
        }
        void get().load()
        return
      }
      case 'deviceRevoked':
        set((state) => ({ devices: state.devices.filter((device) => device.deviceId !== event.deviceId) }))
        return
      case 'deviceSeen':
        set((state) => ({
          devices: state.devices.map((device) =>
            device.deviceId === event.deviceId
              ? { ...device, connected: event.connected, lastSeenAt: event.lastSeenAt }
              : device
          )
        }))
    }
  }
}))

export function connectMobile(): () => void {
  void useMobileStore.getState().load()
  return window.api.mobile.onEvent((event) => useMobileStore.getState().applyEvent(event))
}

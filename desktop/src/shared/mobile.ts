/**
 * Shared phone-access types and pure helpers, imported by the main process and the
 * renderer alike, so no Node or Electron APIs.
 */

import type { HubEvent, HubMobileDevice, HubMobilePairing, HubMobileState } from '@dotcraft/sdk/hub'

export type MobileDevice = Pick<
  HubMobileDevice,
  'deviceId' | 'displayName' | 'platform' | 'osVersion' | 'pairedAt' | 'lastSeenAt' | 'connected'
>

export type MobileGateway = Pick<HubMobileState, 'state' | 'failureCode' | 'failureMessage' | 'port' | 'relay'>

export interface MobileStatus extends MobileGateway {
  devices: MobileDevice[]
}

export interface MobileStatusResult {
  supported: boolean
  status: MobileStatus | null
  error?: string
}

export type MobilePairing = HubMobilePairing

export interface MobileFailure {
  code: string
  message: string
}

export type MobileResult<T> = { ok: true; value: T } | { ok: false; failure: MobileFailure }

export type MobileEvent =
  | { kind: 'stateChanged'; status: MobileStatus }
  | { kind: 'devicePaired'; deviceId: string; displayName: string; pairingId: string }
  | { kind: 'deviceRevoked'; deviceId: string }
  | { kind: 'deviceSeen'; deviceId: string; lastSeenAt: string; connected: boolean }

export function toMobileStatus({ state, failureCode, failureMessage, port, relay, devices }: HubMobileState): MobileStatus {
  return {
    state,
    failureCode,
    failureMessage,
    port,
    relay: relay && { url: relay.url, state: relay.state },
    devices: devices.map(({ deviceId, displayName, platform, osVersion, pairedAt, lastSeenAt, connected }) => ({
      deviceId,
      displayName,
      platform,
      osVersion,
      pairedAt,
      lastSeenAt,
      connected
    }))
  }
}

export function toMobileEvent({ kind, data }: HubEvent): MobileEvent | null {
  switch (kind) {
    case 'mobile.stateChanged':
      return { kind: 'stateChanged', status: toMobileStatus(data as HubMobileState) }
    case 'mobile.devicePaired': {
      const { deviceId, displayName, pairingId } = data as { deviceId: string; displayName: string; pairingId: string }
      return { kind: 'devicePaired', deviceId, displayName, pairingId }
    }
    case 'mobile.deviceRevoked':
      return { kind: 'deviceRevoked', deviceId: (data as { deviceId: string }).deviceId }
    case 'mobile.deviceSeen': {
      const { deviceId, lastSeenAt, connected } = data as { deviceId: string; lastSeenAt: string; connected: boolean }
      return { kind: 'deviceSeen', deviceId, lastSeenAt, connected }
    }
    default:
      return null
  }
}

export function pairingSecondsLeft(pairing: MobilePairing): number {
  return Math.max(0, Math.ceil((Date.parse(pairing.expiresAt) - Date.now()) / 1000))
}

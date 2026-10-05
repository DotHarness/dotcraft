import type { AppRuntime } from '../app-state/runtime'
import { MobileSession } from '../core/session'
import { FakeComputer } from './fakeComputer'
import { FakeNetwork } from './fakeNetwork'
import { MemoryCredentials, MemoryStorage, pairedRecord } from './memory'
import { buildBoxSeed, createStudio, DEMO_CREDENTIAL, pairingUrl, STUDIO_FINGERPRINT } from './seed'

const UNPAIRED = new Set(['unpaired', 'pair-invalid', 'pair-unreachable'])
const BOTH = new Set(['two-computers'])
const LATENCY_MS = 120

export function createDemoRuntime(): AppRuntime {
  const query = new URLSearchParams(globalThis.location?.search ?? '')
  const scenario = query.get('demo') ?? 'home'
  const now = new Date()
  const studio = createStudio(now, { chats: scenario !== 'empty', cantStart: scenario === 'cant-start' ? ['chats'] : [] })
  const buildBox = new FakeComputer(buildBoxSeed(now))
  const network = new FakeNetwork([studio, buildBox], scenario === 'slow' ? 2_500 : LATENCY_MS)
  const paired = UNPAIRED.has(scenario) ? [] : BOTH.has(scenario) ? [studio, buildBox] : [studio]
  if (scenario === 'pair-unreachable') studio.reachable = false
  const session = new MobileSession({
    native: network,
    credentials: new MemoryCredentials(Object.fromEntries(paired.map((computer) => [computer.id, DEMO_CREDENTIAL]))),
    storage: new MemoryStorage(paired.length > 0 ? pairedRecord(paired, new Date(Date.now() - 5 * 86_400_000).toISOString()) : null),
    device: { displayName: 'Demo phone', platform: 'android', osVersion: '16', appVersion: '0.8.0' },
  })

  const project = (name: string) => studio.projects.find((entry) => entry.name === name)?.id ?? name
  const controls = {
    studio,
    buildBox,
    network,
    session,
    goOffline() {
      studio.reachable = false
      studio.dropConnections()
    },
    goOnline() {
      studio.reachable = true
      studio.gatewayOn = true
      network.latencyMs = LATENCY_MS
      session.networkChanged(`demo/${Date.now()}`)
    },
    stall() {
      studio.dropConnections()
      setTimeout(() => {
        network.latencyMs = 3_600_000
      }, LATENCY_MS * 2)
    },
    gatewayOff: () => studio.turnGatewayOff(),
    revoke: () => studio.revokeAll(),
    changeIdentity() {
      studio.certificate = STUDIO_FINGERPRINT.split('').reverse().join('')
      studio.dropConnections()
    },
    stopProject: (name: string) => studio.stopProject(project(name)),
    startProject: (name: string) => studio.startProject(project(name)),
  }
  ;(globalThis as { dotcraftDemo?: typeof controls }).dotcraftDemo = controls

  const scripts: Record<string, () => void> = {
    offline: controls.goOffline,
    connecting: controls.stall,
    'access-off': controls.gatewayOff,
    revoked: controls.revoke,
    identity: controls.changeIdentity,
  }
  const script = scripts[scenario]
  if (script) {
    const unsubscribe = session.store.subscribe(() => {
      const state = session.store.getState().computers[studio.id]
      if (!state || state.link !== 'online' || state.syncing || !state.syncedAt) return
      unsubscribe()
      setTimeout(script, 300)
    })
  }

  return {
    session,
    locale: query.get('lang') ?? undefined,
    demo: {
      scanPayload() {
        if (session.store.getState().order.length > 0) return pairingUrl(buildBox, 'build-box-code')
        if (scenario === 'pair-invalid') return pairingUrl(studio, 'used-code')
        studio.addPairingCode('demo-code')
        return pairingUrl(studio, 'demo-code')
      },
    },
  }
}

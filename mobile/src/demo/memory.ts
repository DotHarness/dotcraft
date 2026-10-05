import type { CredentialStore, StateStorage } from '../core/session'
import type { PersistedState } from '../core/state'
import type { FakeComputer } from './fakeComputer'

export class MemoryStorage implements StateStorage {
  constructor(public value: PersistedState | null = null) {}
  async load() {
    return this.value ? (JSON.parse(JSON.stringify(this.value)) as PersistedState) : null
  }
  async save(state: PersistedState) {
    this.value = JSON.parse(JSON.stringify(state)) as PersistedState
  }
  async clear() {
    this.value = null
  }
}

export class MemoryCredentials implements CredentialStore {
  readonly values: Map<string, string>

  constructor(values: Record<string, string> = {}) {
    this.values = new Map(Object.entries(values))
  }
  async get(computerId: string) {
    return this.values.get(computerId) ?? null
  }
  async set(computerId: string, credential: string) {
    this.values.set(computerId, credential)
  }
  async clear(computerId: string) {
    this.values.delete(computerId)
  }
}

export function pairedRecord(computers: FakeComputer[], pairedAt: string): PersistedState {
  const state: PersistedState = { order: computers.map((computer) => computer.id), selected: computers[0]?.id ?? null, computers: {} }
  for (const computer of computers) {
    state.computers[computer.id] = {
      computer: {
        id: computer.id,
        name: computer.name,
        version: computer.version,
        port: computer.port,
        fingerprint: computer.certificate,
        addresses: computer.addresses,
        relay: computer.relay,
        lastAddress: null,
        deviceId: 'dev_demo',
        pairedAt,
      },
      accessOff: false,
      syncedAt: null,
      projects: [],
      chats: {},
      details: {},
    }
  }
  return state
}

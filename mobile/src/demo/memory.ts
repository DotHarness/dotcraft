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
  constructor(public value: string | null = null) {}
  async get() {
    return this.value
  }
  async set(credential: string) {
    this.value = credential
  }
  async clear() {
    this.value = null
  }
}

export function pairedRecord(computer: FakeComputer, pairedAt: string): PersistedState {
  return {
    computer: {
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

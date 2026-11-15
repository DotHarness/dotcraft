import { create } from 'zustand'
import type {
  CodexRemoteClient,
  CodexRemoteEvent,
  CodexRemoteFailure,
  CodexRemotePairing,
  CodexRemoteStatus
} from '../../shared/codexRemote'

export type CodexPairingFlow =
  | { step: 'idle' }
  | { step: 'creating' }
  | { step: 'code'; pairing: CodexRemotePairing }
  | { step: 'error'; failure: CodexRemoteFailure }
  | { step: 'paired' }

interface CodexRemoteState {
  status: CodexRemoteStatus | null
  loadFailure: CodexRemoteFailure | null
  pendingEnabled: boolean | null
  pairing: CodexPairingFlow
  clients: CodexRemoteClient[] | null
  clientsFailure: CodexRemoteFailure | null
  revokingClientId: string | null
}

interface CodexRemoteStore extends CodexRemoteState {
  load(): Promise<void>
  setEnabled(enabled: boolean): Promise<CodexRemoteFailure | null>
  loadClients(): Promise<void>
  revokeClient(clientId: string): Promise<CodexRemoteFailure | null>
  startPairing(): Promise<void>
  closePairing(): void
  applyEvent(event: CodexRemoteEvent): void
}

const IDLE: CodexPairingFlow = { step: 'idle' }
let pairingRequest = 0
let statusRevision = 0
let clientsRequest = 0
let connections = 0
let stopEvents: (() => void) | null = null

function failureOf(error: unknown): CodexRemoteFailure {
  return {
    code: (error as { code?: string })?.code ?? 'hubUnavailable',
    message: error instanceof Error ? error.message : String(error)
  }
}

function setStatus(status: CodexRemoteStatus, extra: Partial<CodexRemoteState> = {}): void {
  const store = useCodexRemoteStore
  const { status: previous, clients } = store.getState()
  const stale = previous?.state !== 'on' || clients === null
  store.setState({ status, loadFailure: null, ...extra })
  if (status.state !== 'on') {
    clientsRequest += 1
    store.setState({ clients: null, clientsFailure: null, revokingClientId: null })
  } else if (stale) {
    void store.getState().loadClients()
  }
}

export const useCodexRemoteStore = create<CodexRemoteStore>((set, get) => ({
  status: null,
  loadFailure: null,
  pendingEnabled: null,
  pairing: IDLE,
  clients: null,
  clientsFailure: null,
  revokingClientId: null,

  async load() {
    const revision = ++statusRevision
    try {
      const result = await window.api.codexRemote.status()
      if (revision !== statusRevision) return
      if (result.ok) setStatus(result.value)
      else set({ loadFailure: result.failure })
    } catch (error) {
      if (revision === statusRevision) set({ loadFailure: failureOf(error) })
    }
  },

  async setEnabled(enabled) {
    const revision = ++statusRevision
    set({ pendingEnabled: enabled })
    let failure: CodexRemoteFailure
    try {
      const result = await (enabled ? window.api.codexRemote.enable() : window.api.codexRemote.disable())
      if (result.ok) {
        if (revision === statusRevision) setStatus(result.value, { pendingEnabled: null })
        else set({ pendingEnabled: null })
        return null
      }
      failure = result.failure
    } catch (error) {
      failure = failureOf(error)
    }
    set({ pendingEnabled: null, ...(revision === statusRevision ? { loadFailure: failure } : {}) })
    return failure
  },

  async loadClients() {
    const request = ++clientsRequest
    try {
      const result = await window.api.codexRemote.listClients()
      if (request !== clientsRequest) return
      set(result.ok ? { clients: result.value, clientsFailure: null } : { clientsFailure: result.failure })
    } catch (error) {
      if (request === clientsRequest) set({ clientsFailure: failureOf(error) })
    }
  },

  async revokeClient(clientId) {
    set({ revokingClientId: clientId })
    let failure: CodexRemoteFailure
    try {
      const result = await window.api.codexRemote.revokeClient(clientId)
      if (result.ok) {
        clientsRequest += 1
        set((state) => ({
          revokingClientId: null,
          clients: state.clients?.filter((client) => client.clientId !== clientId) ?? null
        }))
        return null
      }
      failure = result.failure
    } catch (error) {
      failure = failureOf(error)
    }
    set({ revokingClientId: null })
    return failure
  },

  async startPairing() {
    const request = ++pairingRequest
    set({ pairing: { step: 'creating' } })
    try {
      const result = await window.api.codexRemote.createPairing()
      if (request !== pairingRequest) return
      set({ pairing: result.ok ? { step: 'code', pairing: result.value } : { step: 'error', failure: result.failure } })
    } catch (error) {
      if (request === pairingRequest) set({ pairing: { step: 'error', failure: failureOf(error) } })
    }
  },

  closePairing() {
    pairingRequest += 1
    set({ pairing: IDLE })
  },

  applyEvent(event) {
    if (event.kind === 'paired' || event.kind === 'clientsChanged') {
      if (event.kind === 'paired' && (get().pairing.step === 'code' || get().pairing.step === 'creating')) {
        pairingRequest += 1
        set({ pairing: { step: 'paired' } })
      }
      if (get().status?.state === 'on') void get().loadClients()
      return
    }
    statusRevision += 1
    const { pairing } = get()
    const stranded = event.status.state !== 'on' && pairing.step !== 'idle' && pairing.step !== 'paired'
    if (stranded) pairingRequest += 1
    setStatus(event.status, stranded ? { pairing: { step: 'error', failure: { code: 'hostOff', message: '' } } } : {})
  }
}))

export function connectCodexRemote(): () => void {
  connections += 1
  if (connections === 1) {
    stopEvents = window.api.codexRemote.onEvent((event) => useCodexRemoteStore.getState().applyEvent(event))
    void useCodexRemoteStore.getState().load()
  }
  let connected = true
  return () => {
    if (!connected) return
    connected = false
    connections -= 1
    if (connections === 0) {
      stopEvents?.()
      stopEvents = null
      statusRevision += 1
      clientsRequest += 1
      useCodexRemoteStore.setState({ clients: null, clientsFailure: null, revokingClientId: null })
      useCodexRemoteStore.getState().closePairing()
    }
  }
}

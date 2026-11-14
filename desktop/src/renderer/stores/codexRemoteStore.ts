import { create } from 'zustand'
import type {
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
}

interface CodexRemoteStore extends CodexRemoteState {
  load(): Promise<void>
  setEnabled(enabled: boolean): Promise<CodexRemoteFailure | null>
  startPairing(): Promise<void>
  closePairing(): void
  applyEvent(event: CodexRemoteEvent): void
}

const IDLE: CodexPairingFlow = { step: 'idle' }
let pairingRequest = 0
let statusRevision = 0
let connections = 0
let stopEvents: (() => void) | null = null

function failureOf(error: unknown): CodexRemoteFailure {
  return {
    code: (error as { code?: string })?.code ?? 'hubUnavailable',
    message: error instanceof Error ? error.message : String(error)
  }
}

export const useCodexRemoteStore = create<CodexRemoteStore>((set, get) => ({
  status: null,
  loadFailure: null,
  pendingEnabled: null,
  pairing: IDLE,

  async load() {
    const revision = ++statusRevision
    try {
      const result = await window.api.codexRemote.status()
      if (revision !== statusRevision) return
      set(result.ok ? { status: result.value, loadFailure: null } : { loadFailure: result.failure })
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
        set({ pendingEnabled: null, ...(revision === statusRevision ? { status: result.value, loadFailure: null } : {}) })
        return null
      }
      failure = result.failure
    } catch (error) {
      failure = failureOf(error)
    }
    set({ pendingEnabled: null, ...(revision === statusRevision ? { loadFailure: failure } : {}) })
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
    if (event.kind === 'paired') {
      if (get().pairing.step === 'code' || get().pairing.step === 'creating') {
        pairingRequest += 1
        set({ pairing: { step: 'paired' } })
      }
      return
    }
    statusRevision += 1
    const { pairing } = get()
    const stranded = event.status.state !== 'on' && pairing.step !== 'idle' && pairing.step !== 'paired'
    if (stranded) pairingRequest += 1
    set({
      status: event.status,
      loadFailure: null,
      ...(stranded ? { pairing: { step: 'error', failure: { code: 'hostOff', message: '' } } } : {})
    })
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
      useCodexRemoteStore.getState().closePairing()
    }
  }
}

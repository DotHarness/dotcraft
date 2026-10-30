import { afterEach, describe, expect, it, vi } from 'vitest'
import { systemTimers } from './backoff'
import type { HistoryItem } from './history'
import { persistable, SnapshotProjector } from './persistenceSnapshot'
import type { StateStorage } from './session'
import { SessionPersistence } from './sessionPersistence'
import { initialState, reducer, type Action, type ComputerAction, type ComputerRecord, type MobileState, type PersistedState } from './state'
import { createStore } from './store'

const record: ComputerRecord = {
  id: 'pc', name: 'PC', version: null, port: 47610, fingerprint: 'f', addresses: [], relay: null,
  lastAddress: null, deviceId: 'device', pairedAt: '2026-10-25T00:00:00Z',
}

function initial(): MobileState {
  let state = reducer(initialState(), { type: 'paired', computer: record })
  state = { ...state, hydrated: true }
  return state
}

class ControlledStorage implements StateStorage {
  value: PersistedState | null = null
  readonly writes: (PersistedState | null)[] = []
  readonly pending: { finish: () => void; fail: () => void }[] = []
  hold = false

  async load() { return this.value }
  save(value: PersistedState) { return this.write(value) }
  clear() { return this.write(null) }
  private write(value: PersistedState | null): Promise<void> {
    this.writes.push(value)
    if (!this.hold) {
      this.value = value
      return Promise.resolve()
    }
    return new Promise((resolve, reject) => this.pending.push({
      finish: () => { this.value = value; resolve() },
      fail: () => reject(new Error('write failed')),
    }))
  }
}

function setup(state = initial()) {
  vi.useFakeTimers()
  const store = createStore<MobileState, Action>(reducer, state)
  const storage = new ControlledStorage()
  const persistence = new SessionPersistence(storage, systemTimers, store)
  const change = (action: ComputerAction) => store.dispatch({ type: 'computer', computerId: record.id, action })
  const name = (value: string) => change({ type: 'computerSeen', patch: { name: value } })
  return { store, storage, persistence, change, name }
}

afterEach(() => vi.useRealTimers())

function withChat(items: HistoryItem[] = []): MobileState {
  let state = initial()
  const actions: ComputerAction[] = [
    { type: 'chatUpserted', chat: { key: 'p:t', projectId: 'p', threadId: 't', title: 'Chat', updatedAt: null, runtime: null, lastTurnFailed: false, profileId: null, readState: null } },
    { type: 'detailLoaded', key: 'p:t', history: { items, turns: [{ id: 'turn', status: 'running', error: null }], echoes: [] }, profileName: null, config: null, workspacePath: null, effectiveWorkspacePath: null, context: null },
  ]
  for (const action of actions) state = reducer(state, { type: 'computer', computerId: 'pc', action })
  return state
}

describe('session persistence', () => {
  it('saves after persisted data is idle, ignoring unrelated notifications and unsent echoes', async () => {
    const { change, name, storage } = setup(withChat())
    name('New name')
    await vi.advanceTimersByTimeAsync(399)
    expect(storage.writes).toHaveLength(0)
    change({ type: 'usage', projectId: 'p', providerId: 'provider', usage: { windows: [], balances: [], limitReachedKind: null } })
    change({ type: 'signals', key: 'p:t', signals: { compacting: null, retry: null, stopped: ['turn'] } })
    change({ type: 'tasks', key: 'p:t', source: 'shell', tasks: [] })
    change({ type: 'echo', key: 'p:t', echo: { clientId: 'unsent', text: 'not acknowledged', added: false } })
    await vi.advanceTimersByTimeAsync(1)
    expect(storage.writes).toHaveLength(1)
    expect(storage.value?.computers.pc.computer.name).toBe('New name')
    await vi.advanceTimersByTimeAsync(5000)
    expect(storage.writes).toHaveLength(1)
  })

  it('does not postpone a save beyond two seconds of continuous changes', async () => {
    const { name, storage } = setup()
    for (let index = 0; index < 20; index++) {
      name(`Name ${index}`)
      await vi.advanceTimersByTimeAsync(100)
    }
    expect(storage.writes).toHaveLength(1)
    expect(storage.value?.computers.pc.computer.name).toBe('Name 19')
    name('Last name')
    await vi.advanceTimersByTimeAsync(400)
    expect(storage.value?.computers.pc.computer.name).toBe('Last name')
  })

  it('requests terminal snapshots immediately without waiting for the idle timer', async () => {
    const { change, storage } = setup(withChat())
    change({ type: 'history', key: 'p:t', event: { kind: 'turn', turn: { id: 'turn', threadId: 't', status: 'completed', startedAt: '2026-10-25T00:00:00Z' } } })
    await vi.advanceTimersByTimeAsync(0)
    expect(storage.value?.computers.pc.details['p:t'].history.turns[0].status).toBe('completed')
    expect(storage.writes).toHaveLength(1)
  })

  it('serializes asynchronous writes and coalesces pending requests to the newest snapshot', async () => {
    const { name, storage, persistence } = setup()
    storage.hold = true
    name('First')
    const saved = persistence.flush()
    name('Second')
    void persistence.flush()
    name('Newest')
    void persistence.flush()
    expect(storage.writes).toHaveLength(1)
    storage.pending.shift()!.finish()
    await Promise.resolve()
    expect(storage.writes).toHaveLength(2)
    expect(storage.writes[1]?.computers.pc.computer.name).toBe('Newest')
    storage.pending.shift()!.finish()
    await saved
    expect(storage.value?.computers.pc.computer.name).toBe('Newest')
  })

  it('does not lose a flush requested as the previous asynchronous write settles', async () => {
    const { name, storage, persistence } = setup()
    storage.hold = true
    name('First')
    const first = persistence.flush()
    storage.pending.shift()!.finish()
    await Promise.resolve()
    name('Second')
    const second = persistence.flush()
    await Promise.resolve()
    expect(storage.writes).toHaveLength(2)
    storage.pending.shift()!.finish()
    await Promise.all([first, second])
    expect(storage.value?.computers.pc.computer.name).toBe('Second')
  })

  it('clears after an older in-flight save and never restores a forgotten pairing', async () => {
    const { store, name, storage, persistence } = setup()
    storage.hold = true
    name('Old snapshot')
    const saved = persistence.flush()
    store.dispatch({ type: 'forgotten', computerId: 'pc', revoked: false })
    const cleared = persistence.flush()
    expect(storage.writes).toHaveLength(1)
    storage.pending.shift()!.finish()
    await Promise.resolve()
    expect(storage.writes).toEqual([expect.any(Object), null])
    storage.pending.shift()!.finish()
    await Promise.all([saved, cleared])
    expect(storage.value).toBeNull()
    await vi.advanceTimersByTimeAsync(5000)
    expect(storage.writes).toHaveLength(2)
  })

  it('keeps failed work dirty without a retry loop, retrying at the next relevant change', async () => {
    const { name, storage, persistence } = setup()
    storage.hold = true
    name('Failed')
    const failed = persistence.flush()
    storage.pending.shift()!.fail()
    await failed
    await vi.advanceTimersByTimeAsync(5000)
    expect(storage.writes).toHaveLength(1)
    storage.hold = false
    name('Retry latest')
    await vi.advanceTimersByTimeAsync(400)
    expect(storage.value?.computers.pc.computer.name).toBe('Retry latest')
    expect(storage.writes).toHaveLength(2)
  })

  it('continues with a requested clear even when the preceding write fails', async () => {
    const { name, store, storage, persistence } = setup()
    storage.hold = true
    name('Old')
    const pending = persistence.flush()
    store.dispatch({ type: 'forgotten', computerId: 'pc', revoked: true })
    storage.pending.shift()!.fail()
    await Promise.resolve()
    expect(storage.writes).toEqual([expect.any(Object), null])
    storage.pending.shift()!.finish()
    await pending
    expect(storage.value).toBeNull()
  })
})

describe('offline snapshots', () => {
  it('restores an oversized newest Turn whole and removes image bodies and unsent echoes', () => {
    const items: HistoryItem[] = Array.from({ length: 415 }, (_, index) => ({
      id: `i${index}`, turnId: 'turn', type: 'toolResult', status: 'completed',
      payload: { result: `output ${index}`, contentItems: [{ type: 'image', dataBase64: 'large' }, { type: 'text', text: 'kept' }] },
      createdAt: '', completedAt: null, partial: false,
    }))
    const source = withChat(items)
    source.computers.pc.details['p:t'].history.echoes.push({ clientId: 'unsent', text: 'Unsent message', added: false })
    source.computers.pc.details['p:t'].history.turns.unshift({ id: 'old', status: 'completed', error: null })
    const snapshot = persistable(source)
    const restored = reducer(initialState(), { type: 'hydrated', persisted: JSON.parse(JSON.stringify(snapshot)) })
    const history = restored.computers.pc.details['p:t'].history
    expect(history.items).toHaveLength(415)
    expect(history.turns.map((turn) => turn.id)).toEqual(['turn'])
    expect(history.items[0].payload).toEqual({ result: 'output 0', contentItems: [{ type: 'text', text: 'kept' }] })
    expect(history.echoes).toEqual([])
    expect(restored.computers.pc.details['p:t'].loading).toBe(true)
  })

  it('reuses unchanged detail snapshots and their trimmed history when only metadata changes', () => {
    const state = withChat()
    const projector = new SnapshotProjector()
    const first = projector.project(state)
    const renamed = reducer(state, { type: 'computer', computerId: 'pc', action: { type: 'computerSeen', patch: { name: 'New' } } })
    const next = projector.project(renamed)
    expect(next.computers.pc.details['p:t']).toBe(first.computers.pc.details['p:t'])
    const context = reducer(renamed, { type: 'computer', computerId: 'pc', action: { type: 'context', key: 'p:t', update: { kind: 'snapshot', snapshot: { tokens: 100, contextWindow: 1000, percentLeft: 0.9 } } } })
    const changed = projector.project(context)
    expect(changed.computers.pc.details['p:t'].history).toBe(next.computers.pc.details['p:t'].history)
    expect(changed.computers.pc.details['p:t']).not.toBe(next.computers.pc.details['p:t'])
  })
})

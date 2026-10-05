import { describe, expect, it } from 'vitest'
import { computerReducer, initialState, reducer, type ComputerAction, type ComputerRecord, type ComputerState } from './state'

const record: ComputerRecord = {
  id: 'pc',
  name: 'PC',
  version: null,
  port: 47610,
  fingerprint: 'f',
  addresses: [],
  relay: null,
  lastAddress: null,
  deviceId: 'dev',
  pairedAt: '2026-10-05T00:00:00.000Z',
}

function run(...actions: ComputerAction[]): ComputerState {
  return actions.reduce(computerReducer, reducer(initialState(), { type: 'paired', computer: record }).computers.pc)
}

describe('pending requests', () => {
  it('replaces a replayed pending request instead of duplicating it', () => {
    const request = { kind: 'approval' as const, requestId: 'r1', approvalType: 'shell', operation: 'ls', target: '/', targetLabel: null, reason: '' }
    const added = run({ type: 'pendingAdded', key: 'a:t', request }, { type: 'pendingAdded', key: 'a:t', request })
    expect(added.pending['a:t']).toHaveLength(1)
    expect(computerReducer(added, { type: 'pendingRemoved', key: 'a:t', requestId: 'r1' }).pending['a:t']).toBeUndefined()
  })
})

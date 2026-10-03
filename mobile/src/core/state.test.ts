import { describe, expect, it } from 'vitest'
import { initialState, reducer, type Action, type MobileState } from './state'

function run(state: MobileState, ...actions: Action[]): MobileState {
  return actions.reduce(reducer, state)
}

describe('pending requests', () => {
  it('replaces a replayed pending request instead of duplicating it', () => {
    const request = { kind: 'approval' as const, requestId: 'r1', approvalType: 'shell', operation: 'ls', target: '/', targetLabel: null, reason: '' }
    let state = run(initialState(), { type: 'pendingAdded', key: 'a:t', request }, { type: 'pendingAdded', key: 'a:t', request })
    expect(state.pending['a:t']).toHaveLength(1)
    state = run(state, { type: 'pendingRemoved', key: 'a:t', requestId: 'r1' })
    expect(state.pending['a:t']).toBeUndefined()
  })
})

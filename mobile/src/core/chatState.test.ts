import { describe, expect, it } from 'vitest'
import { followUpMethod } from './chatState'

describe('follow-up method', () => {
  it('steers a running turn, enqueues during maintenance, and starts when idle', () => {
    expect(followUpMethod(null)).toBe('start')
    expect(followUpMethod({ running: false, busy: false })).toBe('start')
    expect(followUpMethod({ running: true, busy: true, activeTurnId: 't' })).toBe('steer')
    expect(followUpMethod({ running: true, busy: true, waitingOnApproval: true, activeTurnId: 't' })).toBe('steer')
    expect(followUpMethod({ busy: true, maintenanceKind: 'compacting' })).toBe('enqueue')
    expect(followUpMethod({ running: true, busy: true, activeTurnId: 't', maintenanceKind: 'compacting' })).toBe('enqueue')
  })
})

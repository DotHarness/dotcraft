import { beforeEach, describe, expect, it, vi } from 'vitest'

const electronMocks = vi.hoisted(() => {
  const show = vi.fn()
  const on = vi.fn()
  const Notification = Object.assign(
    vi.fn().mockImplementation(() => ({ show, on })),
    { isSupported: vi.fn(() => true) }
  )
  return { Notification, show }
})

vi.mock('electron', () => ({ Notification: electronMocks.Notification }))

import type { WindowAttentionContext } from '../windowAttentionNotifier'
import { createWindowAttentionNotifier } from '../windowAttentionNotifier'

const approval = {
  threadId: 'thread_1',
  turnId: 'turn_1',
  requestId: 'request_1',
  reason: 'Run a command'
}

function context(connectionKind: WindowAttentionContext['connectionKind']): WindowAttentionContext {
  return {
    connectionKind,
    window: { isFocused: () => false } as unknown as WindowAttentionContext['window'],
    settings: { locale: 'en' },
    threads: [{ id: 'thread_1', displayName: 'Fix login' }],
    openThread: vi.fn()
  }
}

describe('window attention notifications', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('leaves Hub-managed connections to the tray and notifies once per request for remote connections', async () => {
    const notify = createWindowAttentionNotifier(async () => true)

    expect(await notify('item/approval/request', approval, context('local'))).toBe(false)
    expect(electronMocks.Notification).not.toHaveBeenCalled()

    const remoteApproval = { ...approval, requestId: 'request_2' }
    expect(await notify('item/approval/request', remoteApproval, context('remote'))).toBe(true)
    expect(await notify('item/approval/request', remoteApproval, context('remote'))).toBe(false)
    expect(electronMocks.Notification).toHaveBeenCalledOnce()
    expect(electronMocks.Notification).toHaveBeenCalledWith({
      title: 'Fix login',
      body: 'Needs your approval'
    })
    expect(electronMocks.show).toHaveBeenCalledOnce()
  })
})

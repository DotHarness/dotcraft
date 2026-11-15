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

function context(
  connectionKind: WindowAttentionContext['connectionKind'],
  viewingThreadId: string | null = null
): WindowAttentionContext {
  return {
    connectionKind,
    settings: { locale: 'en' },
    threads: [{ id: 'thread_1', displayName: 'Fix login' }],
    isViewingThread: (threadId) => threadId === viewingThreadId,
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

  it('notifies about a new-task offer unless the user is viewing its thread', async () => {
    const notify = createWindowAttentionNotifier(async () => true)
    const offer = { threadId: 'thread_1', turnId: 'turn_1', requestId: 'task_00000001' }

    expect(await notify('desktop/newTaskOffer', offer, context('local', 'thread_1'))).toBe(false)
    expect(await notify('desktop/newTaskOffer', { ...offer, requestId: 'task_00000002' }, context('local', 'thread_2'))).toBe(true)
    expect(electronMocks.Notification).toHaveBeenCalledOnce()
  })
})

import { beforeEach, describe, expect, it, vi } from 'vitest'

const electronMocks = vi.hoisted(() => ({
  show: vi.fn(),
  notifications: [] as import('node:events').EventEmitter[]
}))

vi.mock('electron', async () => {
  const { EventEmitter } = await import('node:events')
  return {
    Notification: class extends EventEmitter {
      static isSupported() { return true }
      show = electronMocks.show
      constructor() {
        super()
        electronMocks.notifications.push(this)
      }
    }
  }
})

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
    activation: vi.fn(() => ({ open: vi.fn() }))
  }
}

describe('window attention notifications', () => {
  beforeEach(() => {
    electronMocks.show.mockClear()
    electronMocks.notifications.length = 0
  })

  it('leaves Hub-managed connections to the tray and notifies once per request for remote connections', async () => {
    const notify = createWindowAttentionNotifier(async () => true)

    expect(await notify('item/approval/request', approval, context('local'))).toBe(false)
    expect(electronMocks.notifications).toHaveLength(0)

    const remoteApproval = { ...approval, requestId: 'request_2' }
    const open = vi.fn()
    const activation = vi.fn(() => ({ open }))
    const remote = { ...context('remote'), activation }
    expect(await notify('item/approval/request', remoteApproval, remote)).toBe(true)
    expect(await notify('item/approval/request', remoteApproval, remote)).toBe(false)
    expect(electronMocks.show).toHaveBeenCalledOnce()
    expect(activation).toHaveBeenCalledExactlyOnceWith('thread_1')
    const [notification] = electronMocks.notifications
    notification.emit('close', { reason: 'timedOut' })
    notification.emit('click')
    expect(open).toHaveBeenCalledOnce()
  })

  it('notifies about a new-task offer unless the user is viewing its thread', async () => {
    const notify = createWindowAttentionNotifier(async () => true)
    const offer = { threadId: 'thread_1', turnId: 'turn_1', requestId: 'task_00000001' }

    expect(await notify('desktop/newTaskOffer', offer, context('local', 'thread_1'))).toBe(false)
    expect(await notify('desktop/newTaskOffer', { ...offer, requestId: 'task_00000002' }, context('local', 'thread_2'))).toBe(true)
    expect(electronMocks.show).toHaveBeenCalledOnce()
  })
})

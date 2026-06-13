import { describe, expect, it } from 'vitest'
import {
  createThreadSubscriptionOperationQueue,
  runQueuedThreadUnsubscribe
} from '../utils/threadSubscriptionCoordinator'

function createDeferred(): {
  promise: Promise<void>
  resolve: () => void
  reject: (error: unknown) => void
} {
  let resolve!: () => void
  let reject!: (error: unknown) => void
  const promise = new Promise<void>((res, rej) => {
    resolve = res
    reject = rej
  })
  return { promise, resolve, reject }
}

async function flushPromises(): Promise<void> {
  await Promise.resolve()
  await Promise.resolve()
}

describe('thread subscription coordinator', () => {
  it('serializes operations for the same thread', async () => {
    const queue = createThreadSubscriptionOperationQueue()
    const unblockUnsubscribe = createDeferred()
    const calls: string[] = []

    const unsubscribe = queue.enqueue('thread-a', async () => {
      calls.push('unsubscribe:start')
      await unblockUnsubscribe.promise
      calls.push('unsubscribe:end')
    })
    const subscribe = queue.enqueue('thread-a', async () => {
      calls.push('subscribe')
    })

    await flushPromises()
    expect(calls).toEqual(['unsubscribe:start'])

    unblockUnsubscribe.resolve()
    await Promise.all([unsubscribe, subscribe])

    expect(calls).toEqual(['unsubscribe:start', 'unsubscribe:end', 'subscribe'])
  })

  it('skips a queued unsubscribe when the user has returned to the thread', async () => {
    const queue = createThreadSubscriptionOperationQueue()
    const unblockPriorOperation = createDeferred()
    const calls: string[] = []
    const unsubscribeRequests: string[] = []
    let activeThreadId: string | null = 'thread-b'

    const prior = queue.enqueue('thread-a', async () => {
      calls.push('prior:start')
      await unblockPriorOperation.promise
      calls.push('prior:end')
    })
    const unsubscribe = queue.enqueue('thread-a', async () => {
      const sent = await runQueuedThreadUnsubscribe({
        threadId: 'thread-a',
        getActiveThreadId: () => activeThreadId,
        unsubscribe: async (threadId) => {
          unsubscribeRequests.push(threadId)
        }
      })
      calls.push(sent ? 'unsubscribe:sent' : 'unsubscribe:skipped')
    })

    await flushPromises()
    activeThreadId = 'thread-a'
    unblockPriorOperation.resolve()

    await Promise.all([prior, unsubscribe])

    expect(calls).toEqual(['prior:start', 'prior:end', 'unsubscribe:skipped'])
    expect(unsubscribeRequests).toEqual([])
  })

  it('continues later operations after an earlier operation rejects', async () => {
    const queue = createThreadSubscriptionOperationQueue()
    const calls: string[] = []

    await expect(queue.enqueue('thread-a', async () => {
      calls.push('first')
      throw new Error('boom')
    })).rejects.toThrow('boom')

    await queue.enqueue('thread-a', async () => {
      calls.push('second')
    })

    expect(calls).toEqual(['first', 'second'])
  })
})

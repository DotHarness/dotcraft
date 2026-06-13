export type ThreadSubscriptionOperation = () => Promise<void> | void

export interface ThreadSubscriptionOperationQueue {
  enqueue: (threadId: string, operation: ThreadSubscriptionOperation) => Promise<void>
  clear: (threadId?: string) => void
  pending: (threadId: string) => Promise<void> | null
}

export function createThreadSubscriptionOperationQueue(): ThreadSubscriptionOperationQueue {
  const chains = new Map<string, Promise<void>>()

  return {
    enqueue(threadId, operation) {
      const previous = chains.get(threadId) ?? Promise.resolve()
      const current = previous
        .catch(() => undefined)
        .then(async () => {
          await operation()
        })

      chains.set(threadId, current)
      void current
        .finally(() => {
          if (chains.get(threadId) === current) {
            chains.delete(threadId)
          }
        })
        .catch(() => undefined)

      return current
    },
    clear(threadId) {
      if (threadId == null) {
        chains.clear()
        return
      }
      chains.delete(threadId)
    },
    pending(threadId) {
      return chains.get(threadId) ?? null
    }
  }
}

export async function runQueuedThreadUnsubscribe(options: {
  threadId: string
  getActiveThreadId: () => string | null
  unsubscribe: (threadId: string) => Promise<void>
}): Promise<boolean> {
  if (options.getActiveThreadId() === options.threadId) {
    return false
  }
  await options.unsubscribe(options.threadId)
  return true
}

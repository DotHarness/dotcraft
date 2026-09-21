export type ThreadSubscriptionOperation = () => Promise<void> | void

export interface ThreadSubscriptionTarget {
  threadId: string
  workspaceIdentity: string
  workspaceKey: string
  connectionEpoch: number
}

export interface ActiveThreadSubscriptionState {
  connected: boolean
  activeThreadId: string | null
  workspaceIdentity: string
  workspaceKey: string
  connectionEpoch: number
  threadListWorkspaceKey: string | null
  threadIds: readonly string[]
}

export function resolveActiveThreadSubscriptionTarget(
  state: ActiveThreadSubscriptionState,
  requestedThreadId = state.activeThreadId
): ThreadSubscriptionTarget | null {
  if (!state.connected || !requestedThreadId || state.activeThreadId !== requestedThreadId) return null
  if (!state.workspaceIdentity || !state.workspaceKey) return null
  if (state.threadListWorkspaceKey !== state.workspaceKey) return null
  if (!state.threadIds.includes(requestedThreadId)) return null

  return {
    threadId: requestedThreadId,
    workspaceIdentity: state.workspaceIdentity,
    workspaceKey: state.workspaceKey,
    connectionEpoch: state.connectionEpoch
  }
}

export function threadSubscriptionTargetKey(target: ThreadSubscriptionTarget): string {
  return [
    target.workspaceIdentity,
    target.workspaceKey,
    String(target.connectionEpoch),
    target.threadId
  ].join('\u0000')
}

export function isSameThreadSubscriptionTarget(
  left: ThreadSubscriptionTarget | null | undefined,
  right: ThreadSubscriptionTarget | null | undefined
): boolean {
  return left != null && right != null && threadSubscriptionTargetKey(left) === threadSubscriptionTargetKey(right)
}

export function isThreadSubscriptionConnectionCurrent(
  target: ThreadSubscriptionTarget,
  current: Pick<ThreadSubscriptionTarget, 'workspaceIdentity' | 'workspaceKey' | 'connectionEpoch'>
): boolean {
  return target.workspaceIdentity === current.workspaceIdentity
    && target.workspaceKey === current.workspaceKey
    && target.connectionEpoch === current.connectionEpoch
}

export interface ThreadSubscriptionOperationQueue {
  enqueue: (operationKey: string, operation: ThreadSubscriptionOperation) => Promise<void>
  clear: (operationKey?: string) => void
  pending: (operationKey: string) => Promise<void> | null
}

export function createThreadSubscriptionOperationQueue(): ThreadSubscriptionOperationQueue {
  const chains = new Map<string, Promise<void>>()

  return {
    enqueue(operationKey, operation) {
      const previous = chains.get(operationKey) ?? Promise.resolve()
      const current = previous
        .catch(() => undefined)
        .then(async () => {
          await operation()
        })

      chains.set(operationKey, current)
      void current
        .finally(() => {
          if (chains.get(operationKey) === current) {
            chains.delete(operationKey)
          }
        })
        .catch(() => undefined)

      return current
    },
    clear(operationKey) {
      if (operationKey == null) {
        chains.clear()
        return
      }
      chains.delete(operationKey)
    },
    pending(operationKey) {
      return chains.get(operationKey) ?? null
    }
  }
}

export async function runQueuedThreadUnsubscribe(options: {
  threadId: string
  getActiveThreadId: () => string | null
  isConnectionCurrent?: () => boolean
  unsubscribe: (threadId: string) => Promise<void>
}): Promise<boolean> {
  if (options.isConnectionCurrent?.() === false) {
    return false
  }
  if (options.getActiveThreadId() === options.threadId) {
    return false
  }
  await options.unsubscribe(options.threadId)
  return true
}

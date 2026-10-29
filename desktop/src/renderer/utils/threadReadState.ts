import { readAppServerErrorFields } from '../../shared/appServerError'
import { readStateChangedSince, useThreadStore } from '../stores/threadStore'
import type { ThreadReadState, ThreadSummary } from '../types/thread'

export function withReadStates<T extends ThreadSummary>(
  threads: T[],
  readStates: Record<string, ThreadReadState> | null | undefined,
  requestedAtMark: number
): T[] {
  const listed = useThreadStore.getState().threadList
  return threads.map((thread) => {
    const newer = readStateChangedSince(thread.id, requestedAtMark)
      ? listed.find((entry) => entry.id === thread.id)?.readState
      : undefined
    const readState = newer ?? readStates?.[thread.id]
    return readState ? { ...thread, readState } : thread
  })
}

export function isThreadUnread(thread: Pick<ThreadSummary, 'readState'>): boolean {
  return thread.readState?.firstUnread != null
}

export async function updateThreadReadState(
  threadId: string,
  current: ThreadReadState,
  type: 'read' | 'unread'
): Promise<boolean> {
  const store = useThreadStore.getState()
  try {
    const result = await window.api.appServer.sendRequest('thread/readState/update', {
      threadId,
      expectedRevision: current.revision,
      operation: { type }
    }) as { readState: ThreadReadState }
    store.setThreadReadState(threadId, result.readState)
    return true
  } catch (error) {
    const data = readAppServerErrorFields(error).data
    if (data?.code === 'readStateConflict' && isReadState(data.readState)) {
      store.setThreadReadState(threadId, data.readState)
      return true
    }
    console.error('thread/readState/update failed:', error)
    return false
  }
}

function isReadState(value: unknown): value is ThreadReadState {
  return value != null && typeof value === 'object' && typeof (value as ThreadReadState).revision === 'string'
}

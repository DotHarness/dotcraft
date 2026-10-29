import { readAppServerErrorFields } from '../../shared/appServerError'
import { useThreadStore } from '../stores/threadStore'
import type { ThreadReadState, ThreadSummary } from '../types/thread'

export function withReadStates<T extends ThreadSummary>(
  threads: T[],
  readStates: Record<string, ThreadReadState> | null | undefined
): T[] {
  if (!readStates) return threads
  return threads.map((thread) => {
    const readState = readStates[thread.id]
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
): Promise<void> {
  const store = useThreadStore.getState()
  try {
    const result = await window.api.appServer.sendRequest('thread/readState/update', {
      threadId,
      expectedRevision: current.revision,
      operation: { type }
    }) as { readState: ThreadReadState }
    store.setThreadReadState(threadId, result.readState)
  } catch (error) {
    const data = readAppServerErrorFields(error).data
    if (data?.code === 'readStateConflict' && isReadState(data.readState)) {
      store.setThreadReadState(threadId, data.readState)
      return
    }
    console.error('thread/readState/update failed:', error)
  }
}

function isReadState(value: unknown): value is ThreadReadState {
  return value != null && typeof value === 'object' && typeof (value as ThreadReadState).revision === 'string'
}

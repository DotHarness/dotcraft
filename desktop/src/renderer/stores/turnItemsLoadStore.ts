import { create } from 'zustand'
import { getThreadConversation } from './conversationStore'
import { wireItemToConversationItem } from '../types/conversation'
import { readTurnItems, type HistoryRequest } from '../utils/threadHistory'

export type TurnItemsLoadStatus = 'loading' | 'failed'

interface TurnItemsLoadState {
  statuses: Record<string, TurnItemsLoadStatus>
}

export const useTurnItemsLoadStore = create<TurnItemsLoadState>(() => ({ statuses: {} }))

const requestAppServer: HistoryRequest = (method, params) => window.api.appServer.sendRequest(method, params)
const inFlight = new Map<string, Promise<void>>()

function setStatus(turnId: string, status: TurnItemsLoadStatus | null): void {
  useTurnItemsLoadStore.setState(({ statuses }) => {
    const { [turnId]: _previous, ...rest } = statuses
    return { statuses: status ? { ...rest, [turnId]: status } : rest }
  })
}

/** Replaces a summarized Turn's Items with all of its Items; concurrent calls share one read. */
export function loadTurnItems(threadId: string, turnId: string): Promise<void> {
  const pending = inFlight.get(turnId)
  if (pending) return pending
  setStatus(turnId, 'loading')
  const load = readTurnItems(requestAppServer, threadId, turnId)
    .then((rawItems) => {
      setStatus(turnId, null)
      getThreadConversation(threadId)?.getState().replaceTurnItems(turnId, rawItems.map(wireItemToConversationItem))
    })
    .catch(() => setStatus(turnId, 'failed'))
    .finally(() => inFlight.delete(turnId))
  inFlight.set(turnId, load)
  return load
}

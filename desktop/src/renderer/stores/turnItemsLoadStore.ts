import { create } from 'zustand'
import { getThreadConversation } from './conversationStore'
import { wireItemToConversationItem } from '../types/conversation'
import { createHistoryReadQueue, readTurnItemsPage, type HistoryRequest } from '../utils/threadHistory'

export type TurnItemsLoadStatus = 'loading' | 'failed'

interface TurnItemsLoadState {
  statuses: Record<string, TurnItemsLoadStatus>
}

export const useTurnItemsLoadStore = create<TurnItemsLoadState>(() => ({ statuses: {} }))

const requestAppServer: HistoryRequest = (method, params, options) =>
  window.api.appServer.sendRequest(method, params, undefined, options)
const readDetail = createHistoryReadQueue(4)
const inFlight = new WeakMap<NonNullable<ReturnType<typeof getThreadConversation>>, Map<string, Promise<void>>>()

function setStatus(turnId: string, status: TurnItemsLoadStatus | null): void {
  useTurnItemsLoadStore.setState(({ statuses }) => {
    const { [turnId]: _previous, ...rest } = statuses
    return { statuses: status ? { ...rest, [turnId]: status } : rest }
  })
}

export function loadTurnItems(threadId: string, turnId: string, isNeeded = () => true): Promise<void> {
  const store = getThreadConversation(threadId)
  if (!store) return Promise.resolve()
  let loads = inFlight.get(store)
  if (!loads) {
    loads = new Map()
    inFlight.set(store, loads)
  }
  const pending = loads.get(turnId)
  if (pending) return pending
  const current = (): boolean => getThreadConversation(threadId) === store && isNeeded()
  const selected = store.getState().turns.find(turn => turn.id === turnId)
  if (!selected?.summary || !current()) return Promise.resolve()
  const cursor = selected.summary.nextCursor ?? null
  setStatus(turnId, 'loading')
  const load = readDetail(async () => {
    await readTurnItemsPage(requestAppServer, threadId, turnId, cursor, page => {
      if (!current()) return
      store.getState().applyTurnItemsPage(turnId, page.items.map(wireItemToConversationItem), page.nextCursor)
    })
    if (current()) setStatus(turnId, null)
  }, current)
    .catch(() => { if (current()) setStatus(turnId, 'failed') })
    .finally(() => {
      loads.delete(turnId)
      if (getThreadConversation(threadId) === store && !isNeeded()) setStatus(turnId, null)
    })
  loads.set(turnId, load)
  return load
}

export async function loadAllTurnItems(threadId: string, turnId: string, isNeeded = () => true): Promise<void> {
  const store = getThreadConversation(threadId)
  while (store && getThreadConversation(threadId) === store && isNeeded()) {
    const turn = store.getState().turns.find(candidate => candidate.id === turnId)
    if (!turn?.summary) return
    await loadTurnItems(threadId, turnId, isNeeded)
    if (useTurnItemsLoadStore.getState().statuses[turnId] === 'failed') return
  }
}

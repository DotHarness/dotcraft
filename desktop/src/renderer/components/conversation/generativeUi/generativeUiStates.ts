import type { KnownNotificationPayload } from '../../../../shared/appServerBoundary'
import { readGenerativeUiSavedState, type GenerativeUiSavedState } from './generativeUiProtocol'

export interface GenerativeUiBlockSource {
  threadId: string
  turnId: string
  itemId: string
}

const SAVE_DELAY_MS = 600
const threads = new Map<string, Promise<Map<string, GenerativeUiSavedState>>>()
const retained = new Map<string, number>()
const pendingSaves = new Map<string, { threadId: string; timer: ReturnType<typeof setTimeout>; send: () => void }>()
const savesInFlight = new Map<string, Promise<unknown>>()
const blockListeners = new Map<string, Set<(state: GenerativeUiSavedState) => void>>()
let unsubscribeRemoteStates: (() => void) | null = null

const blockKey = (turnId: string, itemId: string, blockIndex: number): string => `${turnId}\n${itemId}\n${blockIndex}`
const threadBlockKey = (threadId: string, block: string): string => `${threadId}\n${block}`

function readThreadStates(threadId: string): Promise<Map<string, GenerativeUiSavedState>> {
  let states = threads.get(threadId)
  if (!states) {
    states = (savesInFlight.get(threadId) ?? Promise.resolve())
      .then(() => window.api.appServer.sendRequest('generativeUi/state/read', { threadId }))
      .then(result => {
        const map = new Map<string, GenerativeUiSavedState>()
        for (const entry of (result as { states?: Array<{ turnId: string; itemId: string; blockIndex: number; state: unknown }> }).states ?? []) {
          const state = readGenerativeUiSavedState(entry.state)
          if (state) map.set(blockKey(entry.turnId, entry.itemId, entry.blockIndex), state)
        }
        return map
      })
      .catch(() => {
        threads.delete(threadId)
        return new Map<string, GenerativeUiSavedState>()
      })
    threads.set(threadId, states)
  }
  return states
}

function receiveRemoteState(payload: KnownNotificationPayload): void {
  if (payload.method !== 'generativeUi/state/updated') return
  const { threadId, turnId, itemId, blockIndex } = payload.params
  const state = payload.params.state === null ? null : readGenerativeUiSavedState(payload.params.state)
  if (payload.params.state !== null && !state) return
  const block = blockKey(turnId, itemId, blockIndex)
  const key = threadBlockKey(threadId, block)
  clearTimeout(pendingSaves.get(key)?.timer)
  pendingSaves.delete(key)
  void threads.get(threadId)?.then(states => state ? states.set(block, state) : states.delete(block))
  if (state) blockListeners.get(key)?.forEach(listener => listener(state))
}

/** Keeps a thread's saved states cached while one of its blocks is mounted; the last release saves pending changes and forgets the cache. */
export function retainGenerativeUiStates(threadId: string): () => void {
  retained.set(threadId, (retained.get(threadId) ?? 0) + 1)
  unsubscribeRemoteStates ??= window.api.appServer.onNotification(receiveRemoteState)
  return () => {
    const count = (retained.get(threadId) ?? 1) - 1
    if (count > 0) {
      retained.set(threadId, count)
      return
    }
    retained.delete(threadId)
    threads.delete(threadId)
    if (retained.size === 0) {
      unsubscribeRemoteStates?.()
      unsubscribeRemoteStates = null
    }
    for (const pending of [...pendingSaves.values()]) {
      if (pending.threadId !== threadId) continue
      clearTimeout(pending.timer)
      pending.send()
    }
  }
}

export async function loadGenerativeUiState(source: GenerativeUiBlockSource, blockIndex: number): Promise<GenerativeUiSavedState | undefined> {
  const states = await readThreadStates(source.threadId)
  return states.get(blockKey(source.turnId, source.itemId, blockIndex))
}

export function subscribeGenerativeUiState(
  source: GenerativeUiBlockSource,
  blockIndex: number,
  listener: (state: GenerativeUiSavedState) => void
): () => void {
  const key = threadBlockKey(source.threadId, blockKey(source.turnId, source.itemId, blockIndex))
  const listeners = blockListeners.get(key) ?? new Set()
  listeners.add(listener)
  blockListeners.set(key, listeners)
  return () => {
    listeners.delete(listener)
    if (listeners.size === 0 && blockListeners.get(key) === listeners) blockListeners.delete(key)
  }
}

export function saveGenerativeUiState(source: GenerativeUiBlockSource, blockIndex: number, state: GenerativeUiSavedState): void {
  const { threadId } = source
  const block = blockKey(source.turnId, source.itemId, blockIndex)
  const key = threadBlockKey(threadId, block)
  void threads.get(threadId)?.then(states => states.set(block, state))
  clearTimeout(pendingSaves.get(key)?.timer)
  const send = (): void => {
    pendingSaves.delete(key)
    const settled = (savesInFlight.get(threadId) ?? Promise.resolve())
      .then(() => window.api.appServer.sendRequest('generativeUi/state/update', { ...source, blockIndex, state }))
      .catch(() => {})
      .finally(() => {
        if (savesInFlight.get(threadId) === settled) savesInFlight.delete(threadId)
      })
    savesInFlight.set(threadId, settled)
  }
  pendingSaves.set(key, { threadId, timer: setTimeout(send, SAVE_DELAY_MS), send })
}

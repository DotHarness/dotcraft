import { createStore } from 'zustand'
import { getThreadConversation } from './conversationStore'
import { createThreadScopedStores } from './threadScopedStore'
import { useThreadStore } from './threadStore'
import { wireTurnToConversationTurn, type ConversationTurn } from '../types/conversation'
import type { Turn } from '../types/thread'
import { hydrateTurns, listThreadTurns, type HistoryRequest } from '../utils/threadHistory'

export type HistoryGapEdge = 'older' | 'newer'

export interface HistoryGap {
  id: number
  followingTurnId: string
  olderCursor: string
  newerCursor: string | null
}

/** Where a Turn appeared in a descending `thread/turns/list` listing. */
export interface ListedTurnPosition {
  backwardsCursor: string
  offset: number
}

interface ThreadHistoryState {
  threadId: string | null
  generation: number
  headLoaded: boolean
  /** Oldest first. */
  gaps: HistoryGap[]
}

let nextGeneration = 1

const histories = createThreadScopedStores<ThreadHistoryState>((threadId) => createStore<ThreadHistoryState>(() => ({
  threadId,
  generation: nextGeneration++,
  headLoaded: false,
  gaps: []
})))

export const useThreadHistoryStore = histories.useActive
export const activateThreadHistory = histories.activate
export const releaseThreadHistory = histories.release

const requestAppServer: HistoryRequest = (method, params) => window.api.appServer.sendRequest(method, params)
const inFlightEdgeLoads = new Map<string, Promise<void>>()
const layoutChangeListeners = new Set<() => void>()
let nextGapId = 1

function createGap(followingTurnId: string, olderCursor: string, newerCursor: string | null): HistoryGap {
  return { id: nextGapId++, followingTurnId, olderCursor, newerCursor }
}

function edgeCursor(gap: HistoryGap, edge: HistoryGapEdge): string | null {
  return edge === 'older' ? gap.olderCursor : gap.newerCursor
}

function isCurrent(threadId: string, generation: number): boolean {
  return histories.get(threadId)?.getState().generation === generation
}

function conversationTurns(threadId: string): ConversationTurn[] {
  return getThreadConversation(threadId)?.getState().turns ?? []
}

function loadedTurnIds(threadId: string): Set<string> {
  return new Set(conversationTurns(threadId).map((turn) => turn.id))
}

function takeUntilLoaded<T extends { id: string }>(
  turns: readonly T[],
  loaded: ReadonlySet<string>
): { fresh: T[]; met: boolean } {
  const end = turns.findIndex((turn) => loaded.has(turn.id))
  return end < 0 ? { fresh: [...turns], met: false } : { fresh: turns.slice(0, end), met: true }
}

function commit(
  threadId: string,
  chronological: readonly Turn[],
  nextGaps: (gaps: HistoryGap[]) => HistoryGap[]
): void {
  const history = histories.get(threadId)
  const conversation = getThreadConversation(threadId)?.getState()
  if (!history || !conversation) return
  if (histories.active() === history) {
    for (const listener of layoutChangeListeners) listener()
  }
  if (chronological.length > 0) {
    conversation.setTurns(
      [
        ...chronological.map((turn) => wireTurnToConversationTurn(turn as unknown as Record<string, unknown>)),
        ...conversation.turns
      ],
      { preserveExistingRealtime: true, realtimeScopeThreadId: threadId }
    )
  }
  history.setState((state) => ({ gaps: nextGaps(state.gaps) }))
}

/** Called right before history pages change the transcript layout, while the old layout is still mounted. */
export function subscribeHistoryLayoutChange(listener: () => void): () => void {
  layoutChangeListeners.add(listener)
  return () => { layoutChangeListeners.delete(listener) }
}

export function beginThreadHistory(threadId: string | null): void {
  if (threadId) histories.create(threadId)
  histories.activate(threadId)
}

/** Must run before the head page's Turns are merged into the conversation store. */
export function applyThreadHistoryHead(
  threadId: string,
  chronological: readonly { id: string }[],
  olderCursor: string | null
): void {
  const history = histories.get(threadId)
  if (!history) return
  const oldest = chronological[0]
  if (!history.getState().headLoaded) {
    history.setState({
      headLoaded: true,
      gaps: oldest && olderCursor ? [createGap(oldest.id, olderCursor, null)] : []
    })
    return
  }
  const loaded = loadedTurnIds(threadId)
  if (!oldest || !olderCursor || chronological.some((turn) => loaded.has(turn.id))) return
  history.setState((state) => ({
    gaps: [...state.gaps, createGap(oldest.id, olderCursor, null)]
  }))
}

export function restartThreadHistory(
  threadId: string,
  chronological: readonly { id: string }[],
  olderCursor: string | null
): void {
  histories.get(threadId)?.setState({ generation: nextGeneration++, headLoaded: false, gaps: [] })
  applyThreadHistoryHead(threadId, chronological, olderCursor)
}

/** Concurrent calls for the same gap edge share one request. */
export function loadThreadHistoryGap(gapId: number, edge: HistoryGapEdge): Promise<void> {
  const { threadId, generation, gaps } = useThreadHistoryStore.getState()
  const gap = gaps.find((candidate) => candidate.id === gapId)
  const cursor = gap ? edgeCursor(gap, edge) : null
  if (!threadId || !cursor) return Promise.resolve()
  const key = `${generation}\u0000${edge}\u0000${cursor}`
  const pending = inFlightEdgeLoads.get(key)
  if (pending) return pending
  const load = readGapEdge(threadId, generation, edge, cursor)
    .finally(() => { inFlightEdgeLoads.delete(key) })
  inFlightEdgeLoads.set(key, load)
  return load
}

async function readGapEdge(
  threadId: string,
  generation: number,
  edge: HistoryGapEdge,
  cursor: string
): Promise<void> {
  const page = await listThreadTurns(
    requestAppServer,
    threadId,
    cursor,
    edge === 'older' ? 'descending' : 'ascending'
  )
  if (!isCurrent(threadId, generation)) return
  const listed = takeUntilLoaded(page.turns, loadedTurnIds(threadId))
  const hydrated = await hydrateTurns(requestAppServer, threadId, listed.fresh)
  if (!isCurrent(threadId, generation)) return
  const index = histories.get(threadId)?.getState().gaps.findIndex((gap) => edgeCursor(gap, edge) === cursor) ?? -1
  if (index < 0) return

  const { fresh, met } = takeUntilLoaded(hydrated, loadedTurnIds(threadId))
  const nextCursor = listed.met || met || fresh.length === 0 ? null : page.nextCursor
  commit(threadId, edge === 'older' ? [...fresh].reverse() : fresh, (gaps) => {
    if (!nextCursor) return gaps.filter((_gap, position) => position !== index)
    const gap = gaps[index]
    const next = edge === 'older'
      ? { ...gap, followingTurnId: fresh[fresh.length - 1].id, olderCursor: nextCursor }
      : { ...gap, newerCursor: nextCursor }
    return gaps.map((candidate, position) => position === index ? next : candidate)
  })
}

/** Resolves with the listed Turn's id once it is in the conversation store. */
export async function openThreadHistoryAround(
  threadId: string,
  position: ListedTurnPosition
): Promise<string> {
  const opened = histories.get(threadId)?.getState()
  const assertCurrent = (): void => {
    if (
      !opened?.headLoaded ||
      !isCurrent(threadId, opened.generation) ||
      useThreadStore.getState().activeThreadId !== threadId
    ) {
      throw new Error(`thread history for ${threadId} is no longer open`)
    }
  }
  assertCurrent()
  const targetCursor = position.offset === 0
    ? position.backwardsCursor
    : (await listThreadTurns(
        requestAppServer,
        threadId,
        position.backwardsCursor,
        'descending',
        position.offset
      )).nextCursor
  const older = targetCursor
    ? await listThreadTurns(requestAppServer, threadId, targetCursor, 'descending')
    : null
  const target = older?.turns[0]
  if (!older?.backwardsCursor || !target) throw new Error('listed turn is no longer in the thread')
  assertCurrent()
  if (loadedTurnIds(threadId).has(target.id)) return target.id

  const newer = await listThreadTurns(requestAppServer, threadId, older.backwardsCursor, 'ascending')
  assertCurrent()
  const listedLoaded = loadedTurnIds(threadId)
  const olderListed = takeUntilLoaded(older.turns, listedLoaded)
  const newerListed = takeUntilLoaded(newer.turns.filter((turn) => turn.id !== target.id), listedLoaded)
  const hydrated = await hydrateTurns(
    requestAppServer,
    threadId,
    [...olderListed.fresh].reverse().concat(newerListed.fresh)
  )
  assertCurrent()

  const loaded = loadedTurnIds(threadId)
  if (loaded.has(target.id)) return target.id
  const olderSide = takeUntilLoaded(hydrated.slice(0, olderListed.fresh.length).reverse(), loaded)
  const newerSide = takeUntilLoaded(hydrated.slice(olderListed.fresh.length), loaded)
  const segment = [...olderSide.fresh].reverse().concat(newerSide.fresh)
  const olderCursor = olderListed.met || olderSide.met ? null : older.nextCursor
  const newerCursor = newerListed.met || newerSide.met ? null : newer.nextCursor

  commit(threadId, segment, (gaps) => {
    const turns = conversationTurns(threadId)
    const newestIndex = turns.findIndex((turn) => turn.id === segment[segment.length - 1].id)
    const following = turns.slice(newestIndex + 1).find((turn) => loaded.has(turn.id))
    const index = following ? gaps.findIndex((gap) => gap.followingTurnId === following.id) : -1
    if (index < 0) return gaps
    const enclosing = gaps[index]
    return [
      ...gaps.slice(0, index),
      ...(olderCursor ? [createGap(segment[0].id, olderCursor, enclosing.newerCursor)] : []),
      ...(newerCursor ? [{ ...enclosing, newerCursor }] : []),
      ...gaps.slice(index + 1)
    ]
  })
  return target.id
}

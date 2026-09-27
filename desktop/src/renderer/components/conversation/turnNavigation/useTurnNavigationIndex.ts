import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useConversationStore } from '../../../stores/conversationStore'
import { useThreadStore } from '../../../stores/threadStore'
import { useThreadHistoryStore } from '../../../stores/threadHistoryStore'
import { wireItemToConversationItem } from '../../../types/conversation'
import { listThreadTurns, readTurnItems, type HistoryRequest } from '../../../utils/threadHistory'
import {
  buildNavigationEntries,
  derivePreviewEntries,
  type ListedTurn,
  type TurnNavigationEntry,
  type TurnPreview
} from './navigationIndex'

const LISTING_PAGE_SIZE = 100
const LISTING_PAGE_LIMIT = 10
const NO_PREVIEWS: ReadonlyMap<string, TurnPreview> = new Map()

type Listing =
  | { status: 'listed'; turns: ListedTurn[] }
  | { status: 'unanchored' }
  | { status: 'failed' }

export interface TurnNavigationIndex {
  threadId: string | null
  entries: TurnNavigationEntry[]
  /** False while the rail would misstate the thread: unloaded history is not listed yet, or cannot be. */
  railAllowed: boolean
  requestPreview(entry: TurnNavigationEntry): void
}

const requestAppServer: HistoryRequest = (method, params) => window.api.appServer.sendRequest(method, params)

async function listTurnPositions(threadId: string): Promise<Listing> {
  const newestFirst: ListedTurn[] = []
  let cursor: string | null = null
  for (let page = 0; page < LISTING_PAGE_LIMIT; page++) {
    const result = await listThreadTurns(requestAppServer, threadId, cursor, 'descending', LISTING_PAGE_SIZE)
    const backwardsCursor = result.backwardsCursor
    if (result.turns.length > 0 && !backwardsCursor) return { status: 'unanchored' }
    result.turns.forEach((turn, offset) => {
      newestFirst.push({ id: turn.id, position: { backwardsCursor: backwardsCursor ?? '', offset } })
    })
    if (!result.nextCursor) return { status: 'listed', turns: newestFirst.reverse() }
    cursor = result.nextCursor
  }
  return { status: 'failed' }
}

function isCurrentGeneration(generation: number): boolean {
  return useThreadHistoryStore.getState().generation === generation
}

export function useTurnNavigationIndex(): TurnNavigationIndex {
  const threadId = useThreadStore((s) => s.activeThreadId)
  const turns = useConversationStore((s) => s.turns)
  const turnDiffs = useConversationStore((s) => s.turnDiffs)
  const generation = useThreadHistoryStore((s) => s.generation)
  const historyOpen = useThreadHistoryStore((s) => s.threadId === threadId && s.headLoaded)
  const historyComplete = useThreadHistoryStore((s) => s.gaps.length === 0)
  const needsListing = threadId !== null && historyOpen && !historyComplete

  const [listing, setListing] = useState<{ generation: number; listing: Listing } | null>(null)
  const [previews, setPreviews] = useState<{ generation: number; map: ReadonlyMap<string, TurnPreview> } | null>(null)
  const listedGenerationRef = useRef<number | null>(null)
  const previewRequestsRef = useRef<{ generation: number; turnIds: Set<string> }>({ generation, turnIds: new Set() })

  useEffect(() => {
    if (!needsListing || !threadId || listedGenerationRef.current === generation) return
    listedGenerationRef.current = generation
    void listTurnPositions(threadId)
      .catch((error: unknown): Listing => {
        console.error('turn navigation listing failed:', error)
        return { status: 'failed' }
      })
      .then((result) => {
        if (isCurrentGeneration(generation)) setListing({ generation, listing: result })
      })
  }, [generation, needsListing, threadId])

  const requestPreview = useCallback((entry: TurnNavigationEntry): void => {
    if (!threadId || !entry.position || entry.content) return
    if (previewRequestsRef.current.generation !== generation) {
      previewRequestsRef.current = { generation, turnIds: new Set() }
    }
    const requested = previewRequestsRef.current.turnIds
    if (requested.has(entry.turnId)) return
    requested.add(entry.turnId)
    void readTurnItems(requestAppServer, threadId, entry.turnId)
      .then((items): TurnPreview => ({
        status: 'loaded',
        entries: derivePreviewEntries(items.map(wireItemToConversationItem))
      }))
      .catch((): TurnPreview => ({ status: 'failed' }))
      .then((preview) => {
        if (!isCurrentGeneration(generation)) return
        setPreviews((state) => {
          const map = new Map(state?.generation === generation ? state.map : NO_PREVIEWS)
          map.set(entry.turnId, preview)
          return { generation, map }
        })
      })
  }, [generation, threadId])

  const currentListing = listing?.generation === generation ? listing.listing : null
  const currentPreviews = previews?.generation === generation ? previews.map : NO_PREVIEWS
  const index = useMemo(() => {
    if (!needsListing) return { entries: buildNavigationEntries(turns, turnDiffs, null, NO_PREVIEWS), railAllowed: true }
    if (currentListing?.status === 'listed') {
      return { entries: buildNavigationEntries(turns, turnDiffs, currentListing.turns, currentPreviews), railAllowed: true }
    }
    return {
      entries: buildNavigationEntries(turns, turnDiffs, null, NO_PREVIEWS),
      railAllowed: currentListing?.status === 'unanchored'
    }
  }, [currentListing, currentPreviews, needsListing, turnDiffs, turns])

  return { threadId, entries: index.entries, railAllowed: index.railAllowed, requestPreview }
}

import { create } from 'zustand'
import { findInSegments } from '../find/model'
import { listFindSurfaces } from '../find/registry'
import { MAX_FIND_MATCHES, type FindMatch } from '../find/types'

interface FindState {
  open: boolean
  query: string
  matches: FindMatch[]
  /** Every occurrence, including any past {@link MAX_FIND_MATCHES}. */
  totalMatches: number
  isCapped: boolean
  /** Index into `matches`, or -1 when there are none. */
  activeIndex: number
  /** Bumped whenever matches were recomputed, to drive the decoration pass. */
  revision: number
}

interface FindActions {
  openFind(): void
  closeFind(): void
  setQuery(query: string): void
  refresh(): void
  goToNext(): void
  goToPrevious(): void
  searchNow(): void
}

export type FindStore = FindState & FindActions

const EMPTY: Pick<FindState, 'matches' | 'totalMatches' | 'isCapped' | 'activeIndex'> = {
  matches: [],
  totalMatches: 0,
  isCapped: false,
  activeIndex: -1
}

function search(query: string, previousActiveId: string | undefined): Pick<
  FindState,
  'matches' | 'totalMatches' | 'isCapped' | 'activeIndex'
> {
  const trimmed = query.trim()
  if (trimmed.length === 0) return EMPTY

  const matches: FindMatch[] = []
  let totalMatches = 0
  let isCapped = false

  for (const surface of listFindSurfaces()) {
    const result = findInSegments(
      surface.id,
      surface.domain,
      surface.getSegments(),
      trimmed,
      Math.max(0, MAX_FIND_MATCHES - matches.length)
    )
    matches.push(...result.matches)
    totalMatches += result.totalMatches
    if (result.isCapped) isCapped = true
  }

  const restored = previousActiveId === undefined
    ? -1
    : matches.findIndex((match) => match.id === previousActiveId)

  return {
    matches,
    totalMatches,
    isCapped,
    activeIndex: restored !== -1 ? restored : (matches.length > 0 ? 0 : -1)
  }
}

// A search walks every segment of every surface, so running one per keystroke or per
// stream delta would make those surfaces stutter.
const SEARCH_DEBOUNCE_MS = 120

let searchTimer: ReturnType<typeof setTimeout> | undefined

function cancelScheduledSearch(): void {
  if (searchTimer !== undefined) clearTimeout(searchTimer)
  searchTimer = undefined
}

export const useFindStore = create<FindStore>((set, get) => {
  function runSearch(): void {
    cancelScheduledSearch()
    const state = get()
    if (!state.open) return
    const activeId = state.matches[state.activeIndex]?.id
    set({ ...search(state.query, activeId), revision: state.revision + 1 })
  }

  function scheduleSearch(): void {
    cancelScheduledSearch()
    searchTimer = setTimeout(runSearch, SEARCH_DEBOUNCE_MS)
  }

  return {
    open: false,
    query: '',
    ...EMPTY,
    revision: 0,

    openFind: () => {
      const state = get()
      // Not debounced: the overlay is appearing and has nothing to show yet.
      cancelScheduledSearch()
      set({ open: true, ...search(state.query, undefined), revision: state.revision + 1 })
    },

    closeFind: () => {
      cancelScheduledSearch()
      set((state) => ({ open: false, ...EMPTY, revision: state.revision + 1 }))
    },

    setQuery: (query) => {
      set({ query })
      scheduleSearch()
    },

    refresh: () => {
      if (!get().open) return
      scheduleSearch()
    },

    goToNext: () => {
      set((state) => state.matches.length === 0
        ? state
        : {
            activeIndex: (state.activeIndex + 1) % state.matches.length,
            revision: state.revision + 1
          })
    },

    goToPrevious: () => {
      set((state) => state.matches.length === 0
        ? state
        : {
            activeIndex: (state.activeIndex - 1 + state.matches.length) % state.matches.length,
            revision: state.revision + 1
          })
    },

    searchNow: runSearch
  }
})

export function activeFindMatch(state: FindStore): FindMatch | undefined {
  return state.activeIndex >= 0 ? state.matches[state.activeIndex] : undefined
}

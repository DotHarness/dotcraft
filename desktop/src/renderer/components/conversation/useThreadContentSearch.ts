import { useEffect, useState } from 'react'
import type { ThreadSummary } from '../../types/thread'

const SEARCH_DEBOUNCE_MS = 100
const SEARCH_LIMIT = 50
const NO_THREADS: ThreadSummary[] = []

export interface ThreadContentSearch {
  threads: ThreadSummary[]
  pending: boolean
}

export function useThreadContentSearch(query: string, enabled: boolean): ThreadContentSearch {
  const term = enabled ? query.trim() : ''
  const [answer, setAnswer] = useState<{ term: string; threads: ThreadSummary[] }>({ term: '', threads: NO_THREADS })

  useEffect(() => {
    if (!term) return
    let current = true
    const timer = setTimeout(() => {
      void searchThreadContents(term).then((threads) => {
        if (current) setAnswer({ term, threads })
      })
    }, SEARCH_DEBOUNCE_MS)
    return () => {
      current = false
      clearTimeout(timer)
    }
  }, [term])

  const answered = term !== '' && answer.term === term
  return { threads: answered ? answer.threads : NO_THREADS, pending: term !== '' && !answered }
}

async function searchThreadContents(term: string): Promise<ThreadSummary[]> {
  try {
    const result = await window.api.appServer.sendRequest('thread/search', {
      searchTerm: term,
      limit: SEARCH_LIMIT,
      sortKey: 'lastActiveAt',
      archived: false
    })
    return result.data.map((match) => match.thread as unknown as ThreadSummary)
  } catch {
    return NO_THREADS
  }
}

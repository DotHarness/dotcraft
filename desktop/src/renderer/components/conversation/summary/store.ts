import { useEffect, useState } from 'react'
import { startSummaryFeed, type SummaryState } from './feed'
import type { SummaryServices } from './summaryServices'

// Hidden threads keep their feeds live so reopening the Summary shows current state without reloading.
const IDLE_FEED_LIMIT = 4

interface FeedEntry {
  state: SummaryState | null
  readonly listeners: Set<(state: SummaryState) => void>
  stop: () => void
}

const feeds = new Map<string, FeedEntry>()
let feedServices: SummaryServices | null = null

function entryFor(services: SummaryServices, threadId: string): FeedEntry {
  if (feedServices !== services) disposeSummaryFeeds()
  feedServices = services
  const existing = feeds.get(threadId)
  if (existing) {
    feeds.delete(threadId)
    feeds.set(threadId, existing)
    return existing
  }
  const entry: FeedEntry = { state: null, listeners: new Set(), stop: () => undefined }
  feeds.set(threadId, entry)
  entry.stop = startSummaryFeed(services, threadId, (state) => {
    entry.state = state
    for (const listener of entry.listeners) listener(state)
  })
  return entry
}

function evictIdleFeeds(): void {
  let idle = [...feeds].filter(([, entry]) => entry.listeners.size === 0)
  while (idle.length > IDLE_FEED_LIMIT) {
    const [threadId, entry] = idle[0]
    entry.stop()
    feeds.delete(threadId)
    idle = idle.slice(1)
  }
}

export function disposeSummaryFeeds(): void {
  for (const entry of feeds.values()) entry.stop()
  feeds.clear()
  feedServices = null
}

export function useSummaryState(services: SummaryServices, threadId: string): SummaryState | null {
  const [state, setState] = useState<SummaryState | null>(() => entryFor(services, threadId).state)
  useEffect(() => {
    const entry = entryFor(services, threadId)
    setState(entry.state)
    entry.listeners.add(setState)
    return () => {
      entry.listeners.delete(setState)
      evictIdleFeeds()
    }
  }, [services, threadId])
  return state
}

function runningTaskCount(state: SummaryState | null): number {
  if (!state) return 0
  const working = state.subagents.filter((agent) => agent.state === 'working' || agent.state === 'waiting').length
  return state.terminals.length + state.workflows.length + working
}

export function useRunningTaskCount(services: SummaryServices, threadId: string | null): number {
  const [count, setCount] = useState(() => threadId ? runningTaskCount(entryFor(services, threadId).state) : 0)
  useEffect(() => {
    if (!threadId) {
      setCount(0)
      return
    }
    const entry = entryFor(services, threadId)
    const listener = (state: SummaryState): void => setCount(runningTaskCount(state))
    setCount(runningTaskCount(entry.state))
    entry.listeners.add(listener)
    return () => {
      entry.listeners.delete(listener)
      evictIdleFeeds()
    }
  }, [services, threadId])
  return count
}

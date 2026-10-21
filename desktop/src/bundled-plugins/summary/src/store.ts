import type { DesktopPluginHost } from '@dotcraft/plugin'
import { useEffect, useState } from 'react'
import { startSummaryFeed, type SummaryState } from './feed'

// Hidden threads keep their feeds live so reopening the Summary shows current state without reloading.
const IDLE_FEED_LIMIT = 4

interface FeedEntry {
  state: SummaryState | null
  readonly listeners: Set<(state: SummaryState) => void>
  stop: () => void
}

const feeds = new Map<string, FeedEntry>()
let feedHost: DesktopPluginHost | null = null

function entryFor(host: DesktopPluginHost, threadId: string): FeedEntry {
  if (feedHost !== host) disposeSummaryFeeds()
  feedHost = host
  const existing = feeds.get(threadId)
  if (existing) {
    feeds.delete(threadId)
    feeds.set(threadId, existing)
    return existing
  }
  const entry: FeedEntry = { state: null, listeners: new Set(), stop: () => undefined }
  feeds.set(threadId, entry)
  entry.stop = startSummaryFeed(host, threadId, (state) => {
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
  feedHost = null
}

export function useSummaryState(host: DesktopPluginHost, threadId: string): SummaryState | null {
  const [state, setState] = useState<SummaryState | null>(() => entryFor(host, threadId).state)
  useEffect(() => {
    const entry = entryFor(host, threadId)
    setState(entry.state)
    entry.listeners.add(setState)
    return () => {
      entry.listeners.delete(setState)
      evictIdleFeeds()
    }
  }, [host, threadId])
  return state
}

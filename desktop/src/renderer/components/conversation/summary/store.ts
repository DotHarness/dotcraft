import { useCallback, useEffect, useState, useSyncExternalStore } from 'react'
import { startSummaryFeed, type SummaryFeed, type SummaryState } from './feed'
import type { SummaryServices } from './summaryServices'
import { useSubAgentTaskTree } from '../../../hooks/useSubAgentTaskTree'
import { retainThreadTaskFeed } from '../../../stores/threadTaskFeed'

const IDLE_FEED_LIMIT = 4

interface FeedEntry {
  state: SummaryState | null
  readonly listeners: Set<(state: SummaryState) => void>
  feed: SummaryFeed | null
}

const feeds = new Map<string, FeedEntry>()
let feedServices: SummaryServices | null = null
let feedScope: string | null = null

function entryFor(services: SummaryServices, threadId: string): FeedEntry {
  const scope = services.appServer.connectionKey()
  if (feedServices !== services || feedScope !== scope) disposeSummaryFeeds()
  feedServices = services
  feedScope = scope
  const existing = feeds.get(threadId)
  if (existing) {
    feeds.delete(threadId)
    feeds.set(threadId, existing)
    return existing
  }
  const entry: FeedEntry = { state: null, listeners: new Set(), feed: null }
  feeds.set(threadId, entry)
  entry.feed = startSummaryFeed(services, threadId, (state) => {
    entry.state = state
    for (const listener of entry.listeners) listener(state)
  })
  return entry
}

function evictIdleFeeds(): void {
  let idle = [...feeds].filter(([, entry]) => entry.listeners.size === 0)
  while (idle.length > IDLE_FEED_LIMIT) {
    const [threadId, entry] = idle[0]
    entry.feed?.stop()
    feeds.delete(threadId)
    idle = idle.slice(1)
  }
}

export function disposeSummaryFeeds(): void {
  for (const entry of feeds.values()) entry.feed?.stop()
  feeds.clear()
  feedServices = null
  feedScope = null
}

export function stopSummaryTerminal(services: SummaryServices, threadId: string, sessionId: string): Promise<void> {
  return entryFor(services, threadId).feed!.stopTerminal(sessionId)
}

export function stopSummaryWorkflow(services: SummaryServices, threadId: string, runId: string): Promise<void> {
  return entryFor(services, threadId).feed!.stopWorkflow(runId)
}

function useSummaryScope(services: SummaryServices): string {
  const subscribe = useCallback((changed: () => void) => services.appServer.onConnectionChange(changed), [services])
  const snapshot = useCallback(() => services.appServer.connectionKey(), [services])
  return useSyncExternalStore(subscribe, snapshot)
}

export function useSummaryState(services: SummaryServices, threadId: string): SummaryState | null {
  const scope = useSummaryScope(services)
  const [state, setState] = useState<SummaryState | null>(() => entryFor(services, threadId).state)
  useEffect(() => {
    const entry = entryFor(services, threadId)
    setState(entry.state)
    entry.listeners.add(setState)
    if (entry.state?.ready) {
      void entry.feed?.refreshTerminals()
    }
    return () => {
      entry.listeners.delete(setState)
      evictIdleFeeds()
    }
  }, [services, threadId, scope])
  return state
}

export function useRunningTaskCount(services: SummaryServices, threadId: string | null, turnRunning: boolean): number {
  const scope = useSummaryScope(services)
  useEffect(() => {
    if (!threadId) return
    const tasks = retainThreadTaskFeed(services.appServer, threadId)
    void tasks.ensure()
    return tasks.release
  }, [services, threadId, scope])
  const { count, delegates } = useSubAgentTaskTree(threadId)
  return turnRunning ? count : delegates
}

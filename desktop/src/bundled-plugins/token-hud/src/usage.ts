import type { DesktopPluginHost } from '@dotcraft/plugin'
import { ThroughputMeter, type RequestTiming } from './throughput'

export interface UsageState {
  readonly tokensPerSecond: number | null
  readonly waitingForSample: boolean
  readonly cacheHitRate: number | null
  /** Mean host-measured first-output latency over the current turn's model requests. */
  readonly firstTokenLatencyMs: number | null
  readonly threadUsage: ThreadUsageSnapshot | null
}

export interface ThreadUsageGroup {
  readonly model: string | null
  readonly turns: number
  readonly totalTokens: number
  readonly costUsd: number | null
}

export interface ThreadUsageSnapshot {
  readonly threadId: string
  readonly turns: number
  readonly totalTokens: number
  readonly costUsd: number | null
  readonly groups: readonly ThreadUsageGroup[]
}

interface CacheUsage {
  readonly inputTokens: number
  readonly cachedInputTokens: number
}

export const EMPTY_USAGE: UsageState = {
  tokensPerSecond: null,
  waitingForSample: false,
  cacheHitRate: null,
  firstTokenLatencyMs: null,
  threadUsage: null
}

const THREAD_USAGE_DEBOUNCE_MS = 250

const listeners = new Set<(state: UsageState) => void>()
let current = EMPTY_USAGE

export function getUsage(): UsageState {
  return current
}

export function subscribeUsage(listener: (state: UsageState) => void): () => void {
  listeners.add(listener)
  return () => {
    listeners.delete(listener)
  }
}

function update(patch: Partial<UsageState>): void {
  current = { ...current, ...patch }
  for (const listener of listeners) listener(current)
}

export function startUsageFeed(host: DesktopPluginHost): () => void {
  let disposed = false
  let refreshTimer: ReturnType<typeof globalThis.setTimeout> | null = null
  let threadId = host.session.threadId
  let workspacePath = host.session.workspacePath
  let activeTurnId: string | null = null
  let liveCacheUsageSeen = false
  const meter = new ThroughputMeter()

  const showCacheHitRate = (usage: CacheUsage | null): void => {
    update({
      cacheHitRate: usage !== null && usage.inputTokens > 0
        ? Math.min(1, usage.cachedInputTokens / usage.inputTokens)
        : null
    })
  }

  const showThroughput = (): void => {
    const tokensPerSecond = meter.tokensPerSecond()
    const firstTokenLatencyMs = meter.firstTokenLatencyMs()
    update({
      ...(firstTokenLatencyMs === null ? {} : { firstTokenLatencyMs }),
      ...(tokensPerSecond === null ? {} : { tokensPerSecond, waitingForSample: false })
    })
  }

  async function refreshThreadUsage(): Promise<void> {
    const requestedThreadId = threadId
    const requestedWorkspacePath = workspacePath
    if (disposed || !requestedThreadId || !requestedWorkspacePath) return
    try {
      const result = await host.appServer.request('usage/thread', { workspacePath: requestedWorkspacePath, threadId: requestedThreadId })
      if (disposed || threadId !== requestedThreadId || workspacePath !== requestedWorkspacePath) return
      update({ threadUsage: toThreadUsage(result) })
      if (!liveCacheUsageSeen) showCacheHitRate(toCacheUsage(result))
    } catch {
      return
    }
  }

  const scheduleThreadUsage = (delay = THREAD_USAGE_DEBOUNCE_MS): void => {
    if (refreshTimer !== null) globalThis.clearTimeout(refreshTimer)
    refreshTimer = globalThis.setTimeout(() => {
      refreshTimer = null
      void refreshThreadUsage()
    }, delay)
  }

  update({ ...EMPTY_USAGE, waitingForSample: host.session.busy })
  void refreshThreadUsage()

  const stopSession = host.session.onChange((session) => {
    if (session.threadId === threadId && session.workspacePath === workspacePath) return
    threadId = session.threadId
    workspacePath = session.workspacePath
    activeTurnId = null
    liveCacheUsageSeen = false
    meter.startTurn()
    update({ threadUsage: null, waitingForSample: session.busy })
    showCacheHitRate(null)
    void refreshThreadUsage()
  })

  const inActiveTurn = (params: { threadId?: string; turnId?: string | null }): boolean =>
    params.threadId === threadId
    && (activeTurnId === null || params.turnId == null || params.turnId === activeTurnId)

  const stopStarted = host.appServer.onNotification('turn/started', (params) => {
    if (params.turn.threadId !== threadId) return
    activeTurnId = params.turn.id
    meter.startTurn()
    update({ waitingForSample: true })
  })

  const stopDelta = host.appServer.onNotification('item/usage/delta', (params) => {
    if (!inActiveTurn(params)) return
    meter.usage({
      llmCall: positiveNumber(params.turnLlmCalls),
      outputTokens: positiveNumber(params.outputTokens) ?? 0,
      timing: toRequestTiming(params.requestTiming)
    })
    showThroughput()
  })

  const stopTokenUsage = host.appServer.onNotification('thread/tokenUsage/updated', (params) => {
    if (params.threadId !== threadId) return
    const usage = toCacheUsage(params.tokenUsage?.total)
    if (usage === null) return
    liveCacheUsageSeen = true
    showCacheHitRate(usage)
  })

  const finishTurn = (params: { turn: { id: string, threadId: string } }): void => {
    if (params.turn.threadId !== threadId) return
    if (activeTurnId !== null && params.turn.id !== activeTurnId) return
    activeTurnId = null
    update({ waitingForSample: false })
    scheduleThreadUsage(0)
  }
  const stopCompleted = host.appServer.onNotification('turn/completed', finishTurn)
  const stopFailed = host.appServer.onNotification('turn/failed', finishTurn)
  const stopCancelled = host.appServer.onNotification('turn/cancelled', finishTurn)

  return () => {
    disposed = true
    if (refreshTimer !== null) globalThis.clearTimeout(refreshTimer)
    stopSession()
    stopStarted()
    stopDelta()
    stopTokenUsage()
    stopCompleted()
    stopFailed()
    stopCancelled()
  }
}

function numberOrNull(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value >= 0 ? value : null
}

function positiveNumber(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value) && value > 0 ? value : null
}

function toRequestTiming(value: unknown): RequestTiming | null {
  const timing = value as { firstOutputLatencyMs?: unknown; generationMs?: unknown } | null | undefined
  const firstOutputLatencyMs = numberOrNull(timing?.firstOutputLatencyMs)
  const generationMs = numberOrNull(timing?.generationMs)
  return firstOutputLatencyMs === null || generationMs === null ? null : { firstOutputLatencyMs, generationMs }
}

function textOrNull(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null
}

function toCacheUsage(value: unknown): CacheUsage | null {
  const usage = value as { inputTokens?: unknown; cachedInputTokens?: unknown } | null
  const inputTokens = numberOrNull(usage?.inputTokens)
  if (inputTokens === null) return null
  return {
    inputTokens,
    cachedInputTokens: numberOrNull(usage?.cachedInputTokens) ?? 0
  }
}

function toThreadUsage(result: unknown): ThreadUsageSnapshot | null {
  const value = result as { threadId?: unknown; turns?: unknown; totalTokens?: unknown; costUsd?: unknown; groups?: unknown } | null
  if (!value || typeof value.threadId !== 'string') return null
  const groups = Array.isArray(value.groups) ? value.groups : []
  return {
    threadId: value.threadId,
    turns: numberOrNull(value.turns) ?? 0,
    totalTokens: numberOrNull(value.totalTokens) ?? 0,
    costUsd: numberOrNull(value.costUsd),
    groups: groups.map((group: Record<string, unknown>) => ({
      model: textOrNull(group?.model),
      turns: numberOrNull(group?.turns) ?? 0,
      totalTokens: numberOrNull(group?.totalTokens) ?? 0,
      costUsd: numberOrNull(group?.costUsd)
    }))
  }
}

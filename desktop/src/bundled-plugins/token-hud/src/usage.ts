import type { DesktopPluginHost } from '@dotcraft/plugin'
import { ThroughputMeter, type RequestTiming } from './throughput'

export interface UsageState {
  readonly tokensPerSecond: number | null
  readonly waitingForSample: boolean
  readonly totalTokens: number | null
  readonly cacheHitRate: number | null
  /** Mean host-measured first-output latency over the current turn's model requests. */
  readonly firstTokenLatencyMs: number | null
  readonly threadUsage: ThreadUsageSnapshot | null
}

export interface ThreadUsageGroup {
  readonly model: string | null
  readonly turns: number
  readonly totalTokens: number
}

export interface ThreadUsageSnapshot {
  readonly threadId: string
  readonly turns: number
  readonly totalTokens: number
  readonly groups: readonly ThreadUsageGroup[]
}

interface SessionTotals {
  readonly totalTokens: number
  readonly inputTokens: number
  readonly cachedInputTokens: number
}

export const EMPTY_USAGE: UsageState = {
  tokensPerSecond: null,
  waitingForSample: false,
  totalTokens: null,
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
  let activeTurnId: string | null = null
  let liveTotalsSeen = false
  const meter = new ThroughputMeter()

  const showTotals = (totals: SessionTotals | null): void => {
    update({
      totalTokens: totals?.totalTokens ?? null,
      cacheHitRate: totals !== null && totals.inputTokens > 0
        ? Math.min(1, totals.cachedInputTokens / totals.inputTokens)
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
    if (disposed || !requestedThreadId) return
    try {
      const result = await host.appServer.request('usage/thread', { threadId: requestedThreadId })
      if (disposed || threadId !== requestedThreadId) return
      update({ threadUsage: toThreadUsage(result) })
      if (!liveTotalsSeen) showTotals(toSessionTotals(result))
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
    if (session.threadId === threadId) return
    threadId = session.threadId
    activeTurnId = null
    liveTotalsSeen = false
    meter.startTurn()
    update({ threadUsage: null, waitingForSample: session.busy })
    showTotals(null)
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
    const totals = toSessionTotals(params.tokenUsage?.total)
    if (totals === null) return
    liveTotalsSeen = true
    showTotals(totals)
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

function toSessionTotals(value: unknown): SessionTotals | null {
  const usage = value as { totalTokens?: unknown; inputTokens?: unknown; cachedInputTokens?: unknown } | null
  const totalTokens = numberOrNull(usage?.totalTokens)
  if (totalTokens === null) return null
  return {
    totalTokens,
    inputTokens: numberOrNull(usage?.inputTokens) ?? 0,
    cachedInputTokens: numberOrNull(usage?.cachedInputTokens) ?? 0
  }
}

function toThreadUsage(result: unknown): ThreadUsageSnapshot | null {
  const value = result as { threadId?: unknown; turns?: unknown; totalTokens?: unknown; groups?: unknown } | null
  if (!value || typeof value.threadId !== 'string') return null
  const groups = Array.isArray(value.groups) ? value.groups : []
  return {
    threadId: value.threadId,
    turns: numberOrNull(value.turns) ?? 0,
    totalTokens: numberOrNull(value.totalTokens) ?? 0,
    groups: groups.map((group: Record<string, unknown>) => ({
      model: textOrNull(group?.model),
      turns: numberOrNull(group?.turns) ?? 0,
      totalTokens: numberOrNull(group?.totalTokens) ?? 0
    }))
  }
}

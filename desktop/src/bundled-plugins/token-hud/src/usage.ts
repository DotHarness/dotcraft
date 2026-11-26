import type { DesktopPluginHost } from '@dotcraft/plugin'
import type { SpeedComparison } from './speedTrend'
import { ThroughputMeter, type RequestTiming } from './throughput'

interface TimingReadout {
  readonly tokensPerSecond: number | null
  readonly firstTokenLatencyMs: number | null
}

interface ThreadMetrics {
  readonly meter: ThroughputMeter
  turnId: string | null
  timingReadout: TimingReadout
  lastRequestSpeed: number | null
  liveCacheHitRate: number | null
  completedCacheHitRate: number | null
}

export interface UsageState extends TimingReadout {
  readonly timingScope: string | null
  readonly speedComparison: SpeedComparison | null
  readonly waitingForSample: boolean
  readonly cacheHitRate: number | null
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

const EMPTY_TIMING: TimingReadout = {
  tokensPerSecond: null,
  firstTokenLatencyMs: null
}

export const EMPTY_USAGE: UsageState = {
  ...EMPTY_TIMING,
  timingScope: null,
  speedComparison: null,
  waitingForSample: false,
  cacheHitRate: null,
  threadUsage: null
}

const MAX_CACHED_THREADS = 100
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
  const metricsByThread = new Map<string, ThreadMetrics>()
  const scopeForThread = (): string | null =>
    workspacePath && threadId ? JSON.stringify([workspacePath, threadId]) : null

  const selectMetrics = (scope: string | null): ThreadMetrics => {
    const cached = scope === null ? undefined : metricsByThread.get(scope)
    const metrics = cached ?? {
      meter: new ThroughputMeter(),
      turnId: null,
      timingReadout: EMPTY_TIMING,
      lastRequestSpeed: null,
      liveCacheHitRate: null,
      completedCacheHitRate: null
    }
    if (scope !== null) {
      metricsByThread.delete(scope)
      metricsByThread.set(scope, metrics)
      if (metricsByThread.size > MAX_CACHED_THREADS) {
        const oldest = metricsByThread.keys().next()
        if (!oldest.done) metricsByThread.delete(oldest.value)
      }
    }
    return metrics
  }

  let metrics = selectMetrics(scopeForThread())
  const cacheHitRate = (): number | null => metrics.liveCacheHitRate ?? metrics.completedCacheHitRate
  const setMeterTurn = (turnId?: string | null): void => {
    if (turnId == null || metrics.turnId === turnId) return
    metrics.turnId = turnId
    metrics.meter.startTurn()
  }

  const showThroughput = (finished = false): void => {
    const tokensPerSecond = metrics.meter.tokensPerSecond()
    metrics.timingReadout = { tokensPerSecond, firstTokenLatencyMs: metrics.meter.firstTokenLatencyMs() }

    const speed = metrics.meter.latestRequestTokensPerSecond()
    let speedComparison = speed === null ? null : current.speedComparison
    if (speed !== null && speed !== metrics.lastRequestSpeed) {
      speedComparison = metrics.lastRequestSpeed === null ? null : { previous: metrics.lastRequestSpeed, next: speed }
      metrics.lastRequestSpeed = speed
    }

    update({
      ...metrics.timingReadout,
      speedComparison,
      ...(finished || tokensPerSecond !== null ? { waitingForSample: false } : {})
    })
  }

  async function refreshThreadUsage(): Promise<void> {
    const requestedThreadId = threadId
    const requestedWorkspacePath = workspacePath
    if (disposed || !requestedThreadId || !requestedWorkspacePath) return
    try {
      const result = await host.appServer.request('usage/thread', { workspacePath: requestedWorkspacePath, threadId: requestedThreadId })
      if (disposed || threadId !== requestedThreadId || workspacePath !== requestedWorkspacePath) return
      metrics.completedCacheHitRate = toCacheHitRate(result) ?? metrics.completedCacheHitRate
      update({ threadUsage: toThreadUsage(result), cacheHitRate: cacheHitRate() })
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

  update({ ...EMPTY_USAGE, timingScope: scopeForThread(), waitingForSample: host.session.busy })
  void refreshThreadUsage()

  const stopSession = host.session.onChange((session) => {
    if (session.threadId === threadId && session.workspacePath === workspacePath) return
    threadId = session.threadId
    workspacePath = session.workspacePath
    activeTurnId = null
    const timingScope = scopeForThread()
    metrics = selectMetrics(timingScope)
    update({
      ...metrics.timingReadout,
      threadUsage: null,
      cacheHitRate: cacheHitRate(),
      speedComparison: null,
      timingScope,
      waitingForSample: session.busy && metrics.timingReadout.tokensPerSecond === null
    })
    void refreshThreadUsage()
  })

  const inActiveTurn = (params: { threadId?: string; turnId?: string | null }): boolean =>
    params.threadId === threadId
    && (activeTurnId === null || params.turnId == null || params.turnId === activeTurnId)

  const stopStarted = host.appServer.onNotification('turn/started', (params) => {
    if (params.turn.threadId !== threadId) return
    activeTurnId = params.turn.id
    setMeterTurn(activeTurnId)
    update({ waitingForSample: true })
  })

  const stopDelta = host.appServer.onNotification('item/usage/delta', (params) => {
    if (!inActiveTurn(params)) return
    setMeterTurn(params.turnId)
    metrics.meter.usage({
      llmCall: positiveNumber(params.turnLlmCalls),
      outputTokens: positiveNumber(params.outputTokens) ?? 0,
      timing: toRequestTiming(params.requestTiming)
    })
    showThroughput()
  })

  const stopTokenUsage = host.appServer.onNotification('thread/tokenUsage/updated', (params) => {
    if (params.threadId !== threadId) return
    const rate = toCacheHitRate(params.tokenUsage?.total)
    if (rate === null) return
    metrics.liveCacheHitRate = rate
    update({ cacheHitRate: rate })
  })

  const finishTurn = (params: { turn: { id: string, threadId: string } }): void => {
    if (params.turn.threadId !== threadId) return
    if (activeTurnId !== null && params.turn.id !== activeTurnId) return
    activeTurnId = null
    setMeterTurn(params.turn.id)
    showThroughput(true)
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

function toCacheHitRate(value: unknown): number | null {
  const usage = value as { inputTokens?: unknown; cachedInputTokens?: unknown } | null
  const inputTokens = positiveNumber(usage?.inputTokens)
  if (inputTokens === null) return null
  return Math.min(1, (numberOrNull(usage?.cachedInputTokens) ?? 0) / inputTokens)
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

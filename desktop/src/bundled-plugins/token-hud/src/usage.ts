import type { DesktopPluginHost } from '@dotcraft/plugin'

export interface UsageState {
  readonly tokensPerSecond: number | null
  readonly waitingForSample: boolean
  readonly totalTokens: number | null
  readonly cacheHitRate: number | null
  /** Mean first-token latency over the current turn's model requests. */
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

export const EMPTY_USAGE: UsageState = {
  tokensPerSecond: null,
  waitingForSample: false,
  totalTokens: null,
  cacheHitRate: null,
  firstTokenLatencyMs: null,
  threadUsage: null
}

const SUMMARY_REFRESH_MS = 60_000
const SUMMARY_DEBOUNCE_MS = 250

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

export function startUsageFeed(
  host: DesktopPluginHost,
  now: () => number = () => globalThis.performance.now()
): () => void {
  let disposed = false
  let summaryTimer: ReturnType<typeof globalThis.setTimeout> | null = null
  let summaryInFlight = false
  let summaryQueued = false
  let observedTokens = 0
  let observedDurationMs = 0
  let sampleStartedAtMs: number | null = null
  let latencySamples = 0
  let latencyTotalMs = 0
  let waitStartedAtMs: number | null = null
  let activeTurnId: string | null = null

  const refreshSummary = async (): Promise<void> => {
    if (disposed) return
    if (summaryInFlight) {
      summaryQueued = true
      return
    }

    summaryInFlight = true
    try {
      const result = await host.appServer.request('usage/summary', {})
      if (disposed) return
      const totalInputTokens = numberOrNull(result.totalInputTokens)
      update({
        totalTokens: numberOrNull(result.totalTokens),
        cacheHitRate: totalInputTokens !== null && totalInputTokens > 0
          ? ratioOrNull(result.cacheHitRate)
          : null
      })
    } catch {
      return
    } finally {
      summaryInFlight = false
      if (summaryQueued && !disposed) {
        summaryQueued = false
        void refreshSummary()
      }
    }
  }

  const scheduleSummary = (delay = SUMMARY_DEBOUNCE_MS): void => {
    if (summaryTimer !== null) globalThis.clearTimeout(summaryTimer)
    summaryTimer = globalThis.setTimeout(() => {
      summaryTimer = null
      void refreshSummary()
      void refreshThreadUsage()
    }, delay)
  }

  const resetTurn = (waitingForSample: boolean): void => {
    observedTokens = 0
    observedDurationMs = 0
    sampleStartedAtMs = null
    latencySamples = 0
    latencyTotalMs = 0
    waitStartedAtMs = null
    update({ waitingForSample })
  }

  update({ ...EMPTY_USAGE, waitingForSample: host.session.busy })
  void refreshSummary()
  const reconciliationTimer = globalThis.setInterval(() => {
    void refreshSummary()
    void refreshThreadUsage()
  }, SUMMARY_REFRESH_MS)

  let workspacePath = host.session.workspacePath
  let threadId = host.session.threadId

  async function refreshThreadUsage(): Promise<void> {
    const requestedThreadId = threadId
    if (disposed || !requestedThreadId) return
    try {
      const result = await host.appServer.request('usage/thread', { threadId: requestedThreadId })
      if (disposed || threadId !== requestedThreadId) return
      update({ threadUsage: toThreadUsage(result) })
    } catch {
      return
    }
  }

  void refreshThreadUsage()

  const stopSession = host.session.onChange((session) => {
    if (session.workspacePath !== workspacePath) {
      workspacePath = session.workspacePath
      update({ totalTokens: null, cacheHitRate: null })
      scheduleSummary(0)
    }
    if (session.threadId !== threadId) {
      threadId = session.threadId
      activeTurnId = null
      resetTurn(session.busy)
      update({ threadUsage: null })
      void refreshThreadUsage()
    }
  })

  const stopStarted = host.appServer.onNotification('turn/started', (params) => {
    if (params.turn.threadId !== threadId) return
    activeTurnId = params.turn.id
    resetTurn(true)
    waitStartedAtMs = now()
  })

  const observeModelOutput = (params: {
    threadId: string
    turnId?: string | null
  }): void => {
    if (params.threadId !== threadId) return
    if (activeTurnId !== null && params.turnId != null && params.turnId !== activeTurnId) return
    sampleStartedAtMs ??= now()
    if (waitStartedAtMs === null) return
    latencyTotalMs += Math.max(0, now() - waitStartedAtMs)
    latencySamples += 1
    waitStartedAtMs = null
    update({ firstTokenLatencyMs: latencyTotalMs / latencySamples })
  }
  const stopAgentMessage = host.appServer.onNotification('item/agentMessage/delta', observeModelOutput)
  const stopReasoning = host.appServer.onNotification('item/reasoning/delta', observeModelOutput)
  const stopToolArguments = host.appServer.onNotification('item/toolCall/argumentsDelta', observeModelOutput)

  // Anything finishing while a request is pending is local work, not provider
  // latency, so the wait restarts at the last one.
  const stopItemCompleted = host.appServer.onNotification('item/completed', (params) => {
    if (waitStartedAtMs === null) return
    if (params.threadId !== threadId) return
    if (activeTurnId !== null && params.turnId != null && params.turnId !== activeTurnId) return
    waitStartedAtMs = now()
  })

  const stopDelta = host.appServer.onNotification('item/usage/delta', (params) => {
    if (params.threadId !== threadId) return
    if (activeTurnId !== null && params.turnId != null && params.turnId !== activeTurnId) return
    const outputTokens = positiveNumber(params.outputTokens)
    const durationMs = sampleStartedAtMs === null ? null : positiveNumber(now() - sampleStartedAtMs)
    sampleStartedAtMs = null
    // One notification per LLM iteration, so the turn's next request waits from here.
    waitStartedAtMs = now()
    if (outputTokens !== null && durationMs !== null) {
      observedTokens += outputTokens
      observedDurationMs += durationMs
      update({
        tokensPerSecond: observedTokens / (observedDurationMs / 1000),
        waitingForSample: false
      })
    }
    scheduleSummary()
  })

  const finishTurn = (params: { turn: { id: string, threadId: string } }): void => {
    if (params.turn.threadId !== threadId) return
    if (activeTurnId !== null && params.turn.id !== activeTurnId) return
    activeTurnId = null
    sampleStartedAtMs = null
    waitStartedAtMs = null
    update({ waitingForSample: false })
    scheduleSummary(0)
  }
  const stopCompleted = host.appServer.onNotification('turn/completed', finishTurn)
  const stopFailed = host.appServer.onNotification('turn/failed', finishTurn)
  const stopCancelled = host.appServer.onNotification('turn/cancelled', finishTurn)

  return () => {
    disposed = true
    if (summaryTimer !== null) globalThis.clearTimeout(summaryTimer)
    globalThis.clearInterval(reconciliationTimer)
    stopSession()
    stopStarted()
    stopAgentMessage()
    stopReasoning()
    stopToolArguments()
    stopItemCompleted()
    stopDelta()
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

function ratioOrNull(value: unknown): number | null {
  return typeof value === 'number' && Number.isFinite(value)
    ? Math.min(1, Math.max(0, value))
    : null
}

function textOrNull(value: unknown): string | null {
  return typeof value === 'string' && value.trim() !== '' ? value : null
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

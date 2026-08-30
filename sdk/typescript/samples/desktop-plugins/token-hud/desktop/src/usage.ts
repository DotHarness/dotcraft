import type { DesktopPluginHost } from '@dotcraft/plugin'

export interface UsageState {
  readonly tokensPerSecond: number | null
  readonly waitingForSample: boolean
  readonly totalTokens: number | null
  readonly cacheHitRate: number | null
}

export const EMPTY_USAGE: UsageState = {
  tokensPerSecond: null,
  waitingForSample: false,
  totalTokens: null,
  cacheHitRate: null
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
    }, delay)
  }

  const resetTurn = (waitingForSample: boolean): void => {
    observedTokens = 0
    observedDurationMs = 0
    sampleStartedAtMs = null
    update({ tokensPerSecond: null, waitingForSample })
  }

  update({ ...EMPTY_USAGE, waitingForSample: host.session.busy })
  void refreshSummary()
  const reconciliationTimer = globalThis.setInterval(() => void refreshSummary(), SUMMARY_REFRESH_MS)

  let workspacePath = host.session.workspacePath
  let threadId = host.session.threadId
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
    }
  })

  const stopStarted = host.appServer.onNotification('turn/started', (params) => {
    if (params.turn.threadId !== threadId) return
    activeTurnId = params.turn.id
    resetTurn(true)
  })

  const observeModelOutput = (params: {
    threadId: string
    turnId?: string | null
  }): void => {
    if (params.threadId !== threadId) return
    if (activeTurnId !== null && params.turnId != null && params.turnId !== activeTurnId) return
    sampleStartedAtMs ??= now()
  }
  const stopAgentMessage = host.appServer.onNotification('item/agentMessage/delta', observeModelOutput)
  const stopReasoning = host.appServer.onNotification('item/reasoning/delta', observeModelOutput)
  const stopToolArguments = host.appServer.onNotification('item/toolCall/argumentsDelta', observeModelOutput)

  const stopDelta = host.appServer.onNotification('item/usage/delta', (params) => {
    if (params.threadId !== threadId) return
    if (activeTurnId !== null && params.turnId != null && params.turnId !== activeTurnId) return
    const outputTokens = positiveNumber(params.outputTokens)
    const durationMs = sampleStartedAtMs === null ? null : positiveNumber(now() - sampleStartedAtMs)
    sampleStartedAtMs = null
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

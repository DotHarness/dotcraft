import type { SessionItem } from '@dotcraft/sdk/contracts'
import type { SummaryItem } from './sources'
import type { SummaryServices } from './summaryServices'
import { resolveToolKind, type ToolKindSource } from '../../../utils/toolKind'

export interface SummaryTerminal {
  readonly sessionId: string
  readonly command: string
  readonly callId: string | null
  readonly startedAt: string | null
}

interface TerminalSnapshot {
  readonly sessionId?: string
  readonly threadId?: string
  readonly turnId?: string | null
  readonly callId?: string | null
  readonly command?: string
  readonly status?: string
  readonly startedAt?: string
}

const TERMINAL_ENDS = new Set(['completed', 'failed', 'killed', 'timedOut', 'lost'])

export function createTerminalFeed(
  services: SummaryServices,
  threadId: string,
  onChange: (terminals: readonly SummaryTerminal[]) => void
) {
  const connectionKey = services.appServer.connectionKey()
  const running = new Map<string, { snapshot: TerminalSnapshot; revision: number }>()
  const ended = new Set<string>()
  const returned = new Set<string>()
  let activeTurnId: string | null = null
  let revision = 0
  let generation = 0
  let disposed = false
  let inFlight: Promise<void> | null = null
  let queued = false
  const current = (): boolean => !disposed && connectionKey === services.appServer.connectionKey()
  const callKey = (turnId: string | null | undefined, callId: string): string => `${turnId}:${callId}`

  const publish = (): void => {
    if (!current()) return
    onChange([...running].filter(([, { snapshot }]) => !activeTurnId
      || snapshot.turnId !== activeTurnId
      || (snapshot.callId != null && returned.has(callKey(snapshot.turnId, snapshot.callId))))
      .map(([sessionId, { snapshot }]) => ({
        sessionId, command: snapshot.command ?? '', callId: snapshot.callId ?? null, startedAt: snapshot.startedAt ?? null
      })))
  }

  const apply = (snapshot: TerminalSnapshot | undefined, live = true): void => {
    if (!current() || !snapshot?.sessionId || snapshot.threadId !== threadId || ended.has(snapshot.sessionId)) return
    const { sessionId, status } = snapshot
    if (status !== 'running' && !TERMINAL_ENDS.has(status ?? '')) return
    if (live) revision += 1
    if (status === 'running') running.set(sessionId, { snapshot, revision })
    else {
      ended.add(sessionId)
      running.delete(sessionId)
    }
  }

  const refresh = (): Promise<void> => {
    if (!current()) return Promise.resolve()
    if (inFlight) {
      queued = true
      return inFlight
    }
    const requestGeneration = generation
    const work = async (): Promise<void> => {
      do {
        queued = false
        const before = revision
        try {
          const result = await services.appServer.request('terminal/list', { threadId })
          if (!current() || requestGeneration !== generation) return
          const ids = new Set<string>()
          for (const snapshot of result.terminals ?? []) {
            if (snapshot.threadId !== threadId || !snapshot.sessionId) continue
            ids.add(snapshot.sessionId)
            if ((running.get(snapshot.sessionId)?.revision ?? -1) <= before) apply(snapshot, false)
          }
          for (const [id, entry] of running) {
            if (!ids.has(id) && entry.revision <= before) {
              running.delete(id)
              ended.add(id)
            }
          }
          publish()
        } catch {}
      } while (queued && current() && requestGeneration === generation)
    }
    const pending = work().finally(() => { if (inFlight === pending) inFlight = null })
    inFlight = pending
    return pending
  }

  const applyResult = (result: Record<string, unknown>, turnId: string, callId: string): void => {
    if (typeof result.sessionId !== 'string' || typeof result.status !== 'string') return
    const known = running.get(result.sessionId)?.snapshot
    if (known && (known.callId !== callId || known.turnId !== turnId)) return
    if (result.status === 'running' && !known) return
    apply({ ...known, sessionId: result.sessionId, threadId, turnId, callId, status: result.status })
  }

  const addItems = (items: readonly SummaryItem[]): void => {
    if (!current()) return
    for (const item of items) {
      const { payload } = item
      if (item.type !== 'toolResult' || typeof payload.callId !== 'string') continue
      returned.add(callKey(item.turnId, payload.callId))
      if (resolveToolKind(payload as unknown as ToolKindSource) !== 'exec') continue
      const content = payload.structuredContent
      if (!content || typeof content !== 'object') continue
      applyResult(content as Record<string, unknown>, item.turnId, payload.callId)
    }
    publish()
  }

  const stops = (['terminal/started', 'terminal/completed', 'terminal/cleaned'] as const).map((method) =>
    services.appServer.onNotification(method, (params) => {
      apply(params.terminal)
      publish()
    }))
  const stopConnection = services.appServer.onConnectionChange((connected) => {
    generation += 1
    inFlight = null
    queued = false
    if (connected && current()) void refresh()
  })

  return {
    refresh,
    addItems,
    observeCommandExecution(item: Pick<SessionItem, 'type' | 'payload' | 'turnId'>, turnId?: string | null): void {
      if (!current() || item.type !== 'commandExecution' || !item.payload || typeof item.payload !== 'object') return
      const payload = item.payload as Record<string, unknown>
      if (typeof payload.callId !== 'string') return
      applyResult({ ...payload, status: payload.status === 'cancelled' ? 'killed' : payload.status }, turnId ?? item.turnId, payload.callId)
      publish()
    },
    setActiveTurn(turnId: string | null): void {
      if (turnId === activeTurnId) return
      const previous = activeTurnId
      activeTurnId = turnId
      publish()
      if (previous && previous !== turnId) void refresh()
    },
    async stopMany(sessionIds: readonly string[]): Promise<void> {
      if (!current()) return
      const requestGeneration = generation
      const results = await Promise.allSettled(sessionIds.map(async (sessionId) => {
        const result = await services.appServer.request('terminal/stop', { sessionId })
        if (!current() || requestGeneration !== generation) return
        apply(result.terminal)
        publish()
      }))
      if (!current() || requestGeneration !== generation) return
      const failure = results.find((result) => result.status === 'rejected')
      if (!failure) return
      await refresh()
      if (current() && requestGeneration === generation) throw failure.reason
    },
    dispose(): void {
      disposed = true
      generation += 1
      for (const stop of stops) stop()
      stopConnection()
    }
  }
}

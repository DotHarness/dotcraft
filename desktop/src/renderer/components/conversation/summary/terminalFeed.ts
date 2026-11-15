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
  readonly isBackground?: boolean
}

const TERMINAL_ENDS = new Set(['completed', 'failed', 'killed', 'timedOut', 'lost'])

interface TerminalEvidence {
  users: number
  running: Map<string, TerminalSnapshot>
  ended: Map<string, string>
}

const evidenceByConnection = new Map<string, TerminalEvidence>()

function remember(evidence: TerminalEvidence, snapshot: TerminalSnapshot | undefined): void {
  if (!snapshot?.sessionId || !snapshot.threadId) return
  const { sessionId, status } = snapshot
  if (TERMINAL_ENDS.has(status ?? '')) {
    evidence.running.delete(sessionId)
    evidence.ended.set(sessionId, snapshot.threadId)
  } else if (status === 'running' && !evidence.ended.has(sessionId)) {
    evidence.running.set(sessionId, {
      ...snapshot, isBackground: snapshot.isBackground === true || evidence.running.get(sessionId)?.isBackground === true
    })
  }
}

export function createTerminalFeed(
  services: Pick<SummaryServices, 'appServer'>,
  threadId: string,
  onChange: (terminals: readonly SummaryTerminal[]) => void
) {
  const connectionKey = services.appServer.connectionKey()
  const evidence = evidenceByConnection.get(connectionKey) ?? { users: 0, running: new Map(), ended: new Map() }
  evidence.users += 1
  evidenceByConnection.set(connectionKey, evidence)
  const running = new Map<string, { snapshot: TerminalSnapshot; revision: number }>()
  const ended = new Set<string>()
  let revision = 0
  let generation = 0
  let disposed = false
  let inFlight: Promise<boolean> | null = null
  let queued = false
  const current = (): boolean => !disposed && connectionKey === services.appServer.connectionKey()

  const publish = (): void => {
    if (!current()) return
    onChange([...running].filter(([, { snapshot }]) => snapshot.isBackground === true)
      .map(([sessionId, { snapshot }]) => ({
        sessionId, command: snapshot.command ?? '', callId: snapshot.callId ?? null, startedAt: snapshot.startedAt ?? null
      })))
  }

  const apply = (snapshot: TerminalSnapshot | undefined, live = true): void => {
    if (!current() || !snapshot?.sessionId || snapshot.threadId !== threadId || ended.has(snapshot.sessionId)) return
    const { sessionId, status } = snapshot
    if (status !== 'running' && !TERMINAL_ENDS.has(status ?? '')) return
    if (live) revision += 1
    remember(evidence, snapshot)
    if (status === 'running') running.set(sessionId, {
      snapshot: { ...snapshot, isBackground: snapshot.isBackground === true || running.get(sessionId)?.snapshot.isBackground === true },
      revision
    })
    else {
      ended.add(sessionId)
      running.delete(sessionId)
    }
  }

  const refresh = (): Promise<boolean> => {
    if (!current()) return Promise.resolve(false)
    if (inFlight) {
      queued = true
      return inFlight
    }
    const requestGeneration = generation
    const work = async (): Promise<boolean> => {
      let success = false
      do {
        queued = false
        const before = revision
        try {
          const result = await services.appServer.request('terminal/list', { threadId })
          if (!current() || requestGeneration !== generation) return false
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
              evidence.running.delete(id)
              evidence.ended.set(id, threadId)
            }
          }
          publish()
          success = true
        } catch {
          success = false
        }
      } while (queued && current() && requestGeneration === generation)
      if (requestGeneration === generation) inFlight = null
      return success && current() && requestGeneration === generation
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
      if (item.type !== 'toolResult' || typeof payload.callId !== 'string'
        || resolveToolKind(payload as unknown as ToolKindSource) !== 'exec') continue
      const content = payload.structuredContent
      if (content && typeof content === 'object') applyResult(content as Record<string, unknown>, item.turnId, payload.callId)
    }
    publish()
  }

  const stops = (['terminal/started', 'terminal/backgrounded', 'terminal/completed', 'terminal/cleaned'] as const).map((method) =>
    services.appServer.onNotification(method, (params) => {
      if (!current()) return
      remember(evidence, params.terminal)
      apply(params.terminal)
      publish()
    }))
  const stopConnection = services.appServer.onConnectionChange((connected) => {
    generation += 1
    inFlight = null
    queued = false
    if (connected && current()) void refresh()
  })

  for (const [id, owner] of evidence.ended) if (owner === threadId) ended.add(id)
  for (const snapshot of evidence.running.values()) apply(snapshot, false)
  publish()

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
    async stop(sessionId: string): Promise<void> {
      if (!current()) return
      const requestGeneration = generation
      try {
        const result = await services.appServer.request('terminal/stop', { sessionId })
        if (!current() || requestGeneration !== generation) return
        apply(result.terminal)
        publish()
      } catch (error) {
        if (!current() || requestGeneration !== generation) return
        await refresh()
        if (current() && requestGeneration === generation) throw error
      }
    },
    dispose(): void {
      if (disposed) return
      disposed = true
      generation += 1
      for (const stop of stops) stop()
      stopConnection()
      evidence.users -= 1
      if (evidence.users === 0) evidenceByConnection.delete(connectionKey)
    }
  }
}

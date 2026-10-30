import { isComplete, type ChatHistory, type HistoryItem, type HistoryTurn } from './history'
import { createItemProjector } from './transcriptItems'
import { layoutTurn, sameReferences, showsWork, stabilizeEntries } from './transcriptLayout'
import { inTurn, str } from './transcriptTools'
import { capacityRetry, failureEntry, supersededTurns } from './timelineMarkers'
import type { TaskWake, ToolVerb, TranscriptEntry, TranscriptOptions } from './transcriptTypes'
import { createChangesProjector, type ChangesProjector, type TurnChanges } from './turnChanges'
import { userImages, userSegments } from './userSegments'

export type * from './transcriptTypes'
export { baseName } from './transcriptTools'
export { thinkingStatus } from './transcriptItems'

const DONE = new Set(['completed', 'failed', 'cancelled'])

export type LatestActivity = { kind: 'tool'; verb: ToolVerb; subject: string | null } | { kind: 'thinking' } | { kind: 'replying' }

function latestIn(entries: TranscriptEntry[]): LatestActivity | null {
  for (let index = entries.length - 1; index >= 0; index--) {
    const entry = entries[index]
    switch (entry.kind) {
      case 'user':
        return null
      case 'tool':
        return { kind: 'tool', verb: entry.verb, subject: entry.subject }
      case 'thinking':
        if (entry.working && !entries.some(showsWork)) return { kind: 'thinking' }
        break
      case 'assistant':
        if (entry.streaming) return { kind: 'replying' }
        break
      case 'toolGroup':
      case 'activity': {
        const found = latestIn(entry.children)
        if (found) return found
      }
    }
  }
  return null
}

export function latestActivity(history: ChatHistory): LatestActivity | null {
  return latestIn(buildTranscript(history))
}


interface TurnProjection {
  items: readonly HistoryItem[]
  turn: HistoryTurn | undefined
  fallbackOrigin: string | null
  consumesOrigin: boolean
  stopped: boolean
  retry: number | null | undefined
  changes: TurnChanges | null
  entries: TranscriptEntry[]
}

export interface TranscriptProjector {
  project(history: ChatHistory, workspacePath?: string | null, options?: TranscriptOptions): TranscriptEntry[]
  clear(): void
}

export function createTranscriptProjector(sharedChanges?: ChangesProjector): TranscriptProjector {
  const changes = sharedChanges ?? createChangesProjector()
  const itemProjector = createItemProjector()
  let turnsCache = new Map<string, TurnProjection>()
  let previous: TranscriptEntry[] = []
  let echoes: ChatHistory['echoes'] | null = null
  let echoEntries: TranscriptEntry[] = []
  let input: { history: ChatHistory; workspacePath: string | null; stopped: readonly string[] | undefined; spawnedFrom: string | null | undefined } | null = null

  return {
    project(history, workspacePath = null, options = {}) {
      if (input?.history === history && input.workspacePath === workspacePath && input.stopped === options.stopped && input.spawnedFrom === options.spawnedFrom) return previous
      input = { history, workspacePath, stopped: options.stopped, spawnedFrom: options.spawnedFrom }
      const stopped = new Set(options.stopped ?? [])
      const firstTurn = history.olderCursor ? null : (history.turns[0]?.id ?? null)
      const approvals = new Map<string, HistoryItem>()
      const results = new Map<string, HistoryItem>()
      const executions = new Map<string, HistoryItem>()
      const errorTexts = new Map<string, Set<string>>()
      const chunks: { id: string; items: HistoryItem[] }[] = []
      const relatedItems = new Map<string, HistoryItem[]>()
      for (const item of history.items) {
        const related = relatedItems.get(item.turnId)
        if (related) related.push(item)
        else relatedItems.set(item.turnId, [item])
        if (item.type === 'commandExecution' && str(item.payload.callId)) executions.set(inTurn(item.turnId, str(item.payload.callId)), item)
        if (item.type === 'approvalRequest') approvals.set(inTurn(item.turnId, str(item.payload.requestId)), item)
        if (item.type === 'toolResult') results.set(inTurn(item.turnId, str(item.payload.callId)), item)
        if (item.type === 'error') errorTexts.set(item.turnId, (errorTexts.get(item.turnId) ?? new Set()).add(str(item.payload.message).trim()))
        let chunk = chunks.at(-1)
        if (!chunk || chunk.id !== item.turnId) {
          chunk = { id: item.turnId, items: [] }
          chunks.push(chunk)
        }
        chunk.items.push(item)
      }
      const superseded = supersededTurns(history)
      const capacity = capacityRetry(history)
      const turns = new Map(history.turns.map((turn) => [turn.id, turn]))
      const nextCache = new Map<string, TurnProjection>()
      const output: TranscriptEntry[] = []
      const occurrences = new Map<string, number>()
      let threadOrigin = options.spawnedFrom ?? null
      for (const chunk of chunks) {
        if (superseded.has(chunk.id)) continue
        const occurrence = occurrences.get(chunk.id) ?? 0
        occurrences.set(chunk.id, occurrence + 1)
        const key = inTurn(chunk.id, String(occurrence))
        const turn = turns.get(chunk.id)
        const turnChanges = turn?.status === 'completed' ? changes.project(history, chunk.id, workspacePath) : null
        const fallbackOrigin = chunk.id === firstTurn ? threadOrigin : null
        const retry = capacity?.turnId === chunk.id ? capacity.delay : undefined
        const wasStopped = stopped.has(chunk.id)
        const old = turnsCache.get(key)
        const inputs = relatedItems.get(chunk.id)!
        if (old && old.turn === turn && old.fallbackOrigin === fallbackOrigin && old.stopped === wasStopped && old.retry === retry && old.changes === turnChanges && sameReferences(old.items, inputs)) {
          nextCache.set(key, old)
          if (old.consumesOrigin) threadOrigin = null
          output.push(...old.entries)
          continue
        }
        const body: TranscriptEntry[] = []
        const actions: Extract<TranscriptEntry, { kind: 'threadAction' }>[] = []
        let live = false
        let wake: TaskWake | null = null
        let heading: string | null = null
        let origin = fallbackOrigin
        for (const item of chunk.items) {
          if (!isComplete(item)) live = true
          const projection = itemProjector.project(item, {
            result: results.get(inTurn(item.turnId, str(item.payload.callId))),
            execution: executions.get(inTurn(item.turnId, str(item.payload.callId))),
            approval: approvals.get(inTurn(item.turnId, str(item.payload.requestId))),
            turn,
            fallbackOrigin: origin,
          })
          if (projection.entry) {
            body.push(projection.entry)
            if (projection.entry.kind === 'user') {
              origin = null
              threadOrigin = null
            }
          }
          if (projection.wake && !wake && !body.some((entry) => entry.kind === 'user')) wake = projection.wake
          if (projection.heading !== undefined) heading = projection.heading ?? heading
          const action = projection.action
          if (action && !actions.some((entry) => entry.action.kind === action.action.kind && entry.action.threadId === action.action.threadId)) actions.push(action)
        }
        if (turn?.status === 'failed' && turn.error && !errorTexts.get(chunk.id)?.has(turn.error.trim())) body.push(failureEntry(`turn-${chunk.id}`, turn.error, turn.providerError))
        if (retry !== undefined) {
          const last = body.findLastIndex((entry) => entry.kind === 'failure')
          if (last >= 0) body[last] = { ...(body[last] as Extract<TranscriptEntry, { kind: 'failure' }>), retry: { delay: retry } }
        }
        body.push(...actions)
        const summarized = !!turn?.summary && turn.summary.itemCount > chunk.items.length
        const entries = stabilizeEntries(layoutTurn(chunk.id, turn, body, turn ? !DONE.has(turn.status) : live, turnChanges, wake, heading, wasStopped, summarized), old?.entries ?? [])
        const next: TurnProjection = { items: inputs, turn, fallbackOrigin, consumesOrigin: body.some((entry) => entry.kind === 'user'), stopped: wasStopped, retry, changes: turnChanges, entries }
        nextCache.set(key, next)
        output.push(...entries)
      }
      turnsCache = nextCache
      if (echoes !== history.echoes) {
        echoes = history.echoes
        echoEntries = history.echoes.flatMap((echo): TranscriptEntry[] => {
          const segments = userSegments(echo.text, echo.parts ?? null)
          const images = userImages(echo.parts)
          return segments.length > 0 || images.length > 0 ? [{ kind: 'user', id: `echo-${echo.clientId}`, text: echo.text, segments, images, added: echo.added, origin: null, goal: false }] : []
        })
      }
      output.push(...echoEntries)
      previous = sameReferences(output, previous) ? previous : output
      return previous
    },
    clear() {
      turnsCache.clear()
      input = null
      itemProjector.clear()
      if (!sharedChanges) changes.clear()
      previous = []
      echoes = null
      echoEntries = []
    },
  }
}

export function buildTranscript(history: ChatHistory, workspacePath: string | null = null, options: TranscriptOptions = {}): TranscriptEntry[] {
  return createTranscriptProjector().project(history, workspacePath, options)
}

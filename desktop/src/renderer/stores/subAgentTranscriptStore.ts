import { create } from 'zustand'
import { wireTurnToConversationTurn, type ConversationTurn } from '../types/conversation'
import type { ThreadConfigurationWire } from '../types/thread'
import type { FileDiff } from '../types/toolCall'
import type { TurnDiff } from '../types/turnDiff'
import { readThreadHistoryHead } from '../utils/threadHistory'
import {
  appendLiveText,
  applyTranscriptEvent,
  isTerminalTurnEvent,
  mergeHistoryTurns,
  transcriptEventThreadId,
  type TranscriptLiveText
} from './subAgentTranscriptEvents'
import { useThreadStore } from './threadStore'
import { deriveItemDiffs, foldHistoryTurnDiffs, setTurnFileStatus } from './turnDiffs'

const TRANSCRIPT_TURN_LIMIT = 20
const DELTA_FLUSH_MS = 50
const RECONCILE_DELAY_MS = 300

export interface SubAgentTranscript {
  status: 'loading' | 'ready' | 'error'
  turns: ConversationTurn[]
  live: TranscriptLiveText | null
  configuration: ThreadConfigurationWire | null
  workspacePath: string | null
  itemDiffs: Map<string, FileDiff>
  turnDiffs: ReadonlyMap<string, TurnDiff>
  subscribed: boolean | null
}

interface SubAgentTranscriptStore {
  transcripts: Map<string, SubAgentTranscript>
  fetchTranscript(childThreadId: string): Promise<void>
  watchTranscript(childThreadId: string): () => void
  setTurnFileStatus(childThreadId: string, turnId: string, key: string, status: FileDiff['status']): void
  reset(): void
}

function emptyTranscript(status: SubAgentTranscript['status']): SubAgentTranscript {
  return {
    status,
    turns: [],
    live: null,
    configuration: null,
    workspacePath: null,
    itemDiffs: new Map(),
    turnDiffs: new Map(),
    subscribed: null
  }
}

function withTimeline(
  transcript: SubAgentTranscript,
  turns: ConversationTurn[],
  live: TranscriptLiveText | null
): SubAgentTranscript {
  const workspacePath = transcript.workspacePath ?? undefined
  const itemDiffs = deriveItemDiffs(turns, transcript.itemDiffs, workspacePath)
  return {
    ...transcript,
    turns,
    live,
    itemDiffs,
    turnDiffs: foldHistoryTurnDiffs(turns, itemDiffs, transcript.turnDiffs, workspacePath)
  }
}

interface FetchFlight {
  rerun: boolean
}

const fetches = new Map<string, FetchFlight>()
const pendingDeltas = new Map<string, { itemId: string; text: string }[]>()
const deltaTimers = new Map<string, ReturnType<typeof setTimeout>>()
const reconcileTimers = new Map<string, ReturnType<typeof setTimeout>>()

export const useSubAgentTranscriptStore = create<SubAgentTranscriptStore>((set, get) => {
  const update = (childThreadId: string, change: (transcript: SubAgentTranscript) => SubAgentTranscript): void => {
    set((state) => {
      const current = state.transcripts.get(childThreadId) ?? emptyTranscript('loading')
      const next = change(current)
      if (next === current) return state
      const transcripts = new Map(state.transcripts)
      transcripts.set(childThreadId, next)
      return { transcripts }
    })
  }

  const flushDeltas = (childThreadId: string): void => {
    const timer = deltaTimers.get(childThreadId)
    if (timer) clearTimeout(timer)
    deltaTimers.delete(childThreadId)
    const queued = pendingDeltas.get(childThreadId)
    pendingDeltas.delete(childThreadId)
    if (!queued?.length) return
    update(childThreadId, (transcript) => {
      const timeline = queued.reduce(
        (current, delta) => appendLiveText(current, delta.itemId, delta.text),
        { turns: transcript.turns, live: transcript.live }
      )
      return timeline.live === transcript.live ? transcript : { ...transcript, live: timeline.live }
    })
  }

  const queueDelta = (childThreadId: string, itemId: unknown, delta: unknown): void => {
    if (typeof itemId !== 'string' || typeof delta !== 'string') return
    const queued = pendingDeltas.get(childThreadId) ?? []
    queued.push({ itemId, text: delta })
    pendingDeltas.set(childThreadId, queued)
    if (!deltaTimers.has(childThreadId)) {
      deltaTimers.set(childThreadId, setTimeout(() => flushDeltas(childThreadId), DELTA_FLUSH_MS))
    }
  }

  const scheduleReconcile = (childThreadId: string): void => {
    clearTimeout(reconcileTimers.get(childThreadId))
    reconcileTimers.set(childThreadId, setTimeout(() => {
      reconcileTimers.delete(childThreadId)
      void get().fetchTranscript(childThreadId)
    }, RECONCILE_DELAY_MS))
  }

  return {
    transcripts: new Map(),

    async fetchTranscript(childThreadId) {
      if (!childThreadId) return
      const running = fetches.get(childThreadId)
      if (running) {
        running.rerun = true
        return
      }
      const flight: FetchFlight = { rerun: false }
      fetches.set(childThreadId, flight)
      if (!get().transcripts.has(childThreadId)) update(childThreadId, () => emptyTranscript('loading'))
      try {
        do {
          flight.rerun = false
          const { thread } = await readThreadHistoryHead(
            (method, params) => window.api.appServer.sendRequest(method, params),
            childThreadId,
            TRANSCRIPT_TURN_LIMIT
          )
          if (fetches.get(childThreadId) !== flight) return
          const history = (thread.turns ?? []).map((turn) =>
            wireTurnToConversationTurn(turn as unknown as Record<string, unknown>)
          )
          flushDeltas(childThreadId)
          update(childThreadId, (transcript) => {
            const merged = mergeHistoryTurns({ turns: transcript.turns, live: transcript.live }, history)
            return withTimeline({
              ...transcript,
              status: 'ready',
              configuration: thread.configuration ?? null,
              workspacePath: thread.workspacePath || transcript.workspacePath
            }, merged.turns, merged.live)
          })
        } while (flight.rerun)
      } catch {
        if (get().transcripts.get(childThreadId)?.status !== 'ready') {
          update(childThreadId, (transcript) => ({ ...transcript, status: 'error' }))
        }
      } finally {
        if (fetches.get(childThreadId) === flight) fetches.delete(childThreadId)
      }
    },

    watchTranscript(childThreadId) {
      let active = true
      if (!get().transcripts.has(childThreadId)) update(childThreadId, () => emptyTranscript('loading'))

      const stopListening = window.api.appServer.onNotification((payload) => {
        if (!active || payload.foreground === false) return
        const params = (payload.params ?? {}) as Record<string, unknown>
        if (transcriptEventThreadId(params) !== childThreadId) return
        if (payload.method === 'item/agentMessage/delta' || payload.method === 'item/reasoning/delta') {
          queueDelta(childThreadId, params.itemId, params.delta)
          return
        }
        flushDeltas(childThreadId)
        const current = get().transcripts.get(childThreadId)
        if (!current) return
        const next = applyTranscriptEvent({ turns: current.turns, live: current.live }, payload.method, params)
        if (!next) return
        update(childThreadId, (transcript) => withTimeline(transcript, next.turns, next.live))
        if (isTerminalTurnEvent(payload.method)) scheduleReconcile(childThreadId)
      })

      void window.api.appServer
        .sendRequest('thread/subscribe', { threadId: childThreadId, replayRecent: true })
        .then(() => true, () => false)
        .then((subscribed) => {
          if (!active) return
          update(childThreadId, (transcript) => ({ ...transcript, subscribed }))
          void get().fetchTranscript(childThreadId)
        })

      return () => {
        active = false
        stopListening()
        flushDeltas(childThreadId)
        if (useThreadStore.getState().activeThreadId !== childThreadId) {
          void window.api.appServer
            .sendRequest('thread/unsubscribe', { threadId: childThreadId })
            .catch(() => {})
        }
      }
    },

    setTurnFileStatus(childThreadId, turnId, key, status) {
      update(childThreadId, (transcript) => {
        const turnDiffs = setTurnFileStatus(transcript.turnDiffs, turnId, key, status)
        return turnDiffs === transcript.turnDiffs ? transcript : { ...transcript, turnDiffs }
      })
    },

    reset() {
      fetches.clear()
      pendingDeltas.clear()
      for (const timer of deltaTimers.values()) clearTimeout(timer)
      deltaTimers.clear()
      for (const timer of reconcileTimers.values()) clearTimeout(timer)
      reconcileTimers.clear()
      set({ transcripts: new Map() })
    }
  }
})

import { create } from 'zustand'
import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import { wireTurnToConversationTurn, type ConversationTurn } from '../types/conversation'
import type { ThreadConfigurationWire } from '../types/thread'
import type { FileDiff } from '../types/toolCall'
import type { TurnDiff } from '../types/turnDiff'
import { readTurnItemsPage, THREAD_OPENING_PAGE } from '../utils/threadHistory'
import type { AppServerResponseOptions } from '../../shared/appServerBoundary'
import { parseUnifiedDiff } from '../utils/unifiedDiff'
import { useTurnItemsLoadStore } from './turnItemsLoadStore'
import { wireItemToConversationItem } from '../types/conversation'
import { mergeRestoredTurns, mergeTurnItemsPage } from './turnItemsHydration'
import {
  appendLiveText,
  applyTranscriptEvent,
  restoreTranscript,
  transcriptEventThreadId,
  type TranscriptLiveText
} from './subAgentTranscriptEvents'
import { deriveItemDiffs, foldHistoryTurnDiffs, applyLiveTurnDiff, setTurnFileStatus } from './turnDiffs'

const DELTA_FLUSH_MS = 50

export interface SubAgentTranscript {
  status: 'loading' | 'ready' | 'error'
  turns: ConversationTurn[]
  live: TranscriptLiveText | null
  configuration: ThreadConfigurationWire | null
  workspacePath: string | null
  itemDiffs: Map<string, FileDiff>
  turnDiffs: ReadonlyMap<string, TurnDiff>
}

interface SubAgentTranscriptStore {
  transcripts: Map<string, SubAgentTranscript>
  watchTranscript(childThreadId: string): () => void
  loadEarlier(childThreadId: string): Promise<void>
  loadTurnItems(childThreadId: string, turnId: string, isNeeded?: () => boolean, complete?: boolean): Promise<void>
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
    turnDiffs: new Map()
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

interface TranscriptWatch {
  users: number
  loadEarlier(): Promise<void>
  loadTurnItems(turnId: string, isNeeded: () => boolean, complete: boolean): Promise<void>
  stop(): void
}

const watches = new Map<string, TranscriptWatch>()

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

  return {
    transcripts: new Map(),

    watchTranscript(childThreadId) {
      const existing = watches.get(childThreadId)
      if (existing) {
        existing.users += 1
      } else {
        let active = true
        let ready = false
        let attached = false
        let connectionKey: string | null = null
        let turnCursor: string | null = null
        let loadingEarlier = false
        let restoration: Promise<void> = Promise.resolve()
        const itemLoads = new Map<string, Promise<void>>()
        const request = <M extends keyof ClientRequestMethods>(
          method: M, params: ClientRequestMethods[M]['params'],
          options?: AppServerResponseOptions<ClientRequestMethods[M]['result']>
        ): Promise<ClientRequestMethods[M]['result']> => {
          if (!connectionKey) return Promise.reject(new Error('No transcript connection'))
          return window.api.appServer.sendConnectionRequest(connectionKey, method, params, undefined, options)
        }
        let timer: ReturnType<typeof setTimeout> | undefined
        let deltas: { itemId: string; text: string }[] = []
        const attachmentOwner = `transcript:${childThreadId}:${crypto.randomUUID()}`
        const release = (): void => {
          if (!attached || !connectionKey) return
          attached = false
          void window.api.appServer.sendConnectionRequest(
            connectionKey, 'thread/unsubscribe', { threadId: childThreadId }, undefined, { attachmentOwner }
          ).catch(() => {})
        }
        const flush = (): void => {
          clearTimeout(timer)
          timer = undefined
          const queued = deltas
          deltas = []
          if (!active || !queued.length) return
          update(childThreadId, (transcript) => {
            const timeline = queued.reduce(
              (current, delta) => appendLiveText(current, delta.itemId, delta.text),
              { turns: transcript.turns, live: transcript.live }
            )
            return { ...transcript, live: timeline.live }
          })
        }
        update(childThreadId, (transcript) => ({
          ...transcript, status: transcript.turns.length > 0 ? 'ready' : 'loading'
        }))
        const stopListening = window.api.appServer.onNotification((payload) => {
          if (!active || !ready) return
          const params = (payload.params ?? {}) as Record<string, unknown>
          if (transcriptEventThreadId(params) !== childThreadId) return
          if (payload.method === 'item/agentMessage/delta' || payload.method === 'item/reasoning/delta') {
            if (typeof params.itemId !== 'string' || typeof params.delta !== 'string') return
            deltas.push({ itemId: params.itemId, text: params.delta })
            timer ??= setTimeout(flush, DELTA_FLUSH_MS)
            return
          }
          flush()
          const current = get().transcripts.get(childThreadId)
          if (!current) return
          const next = applyTranscriptEvent(current, payload.method, params)
          if (next) update(childThreadId, (transcript) => withTimeline(transcript, next.turns, next.live))
          if (payload.method === 'turn/diff/updated' && typeof params.turnId === 'string' && typeof params.diff === 'string') {
            const turnId = params.turnId
            const diff = params.diff
            update(childThreadId, (transcript) => ({
              ...transcript,
              turnDiffs: applyLiveTurnDiff(
                transcript.turnDiffs, transcript.turns, transcript.itemDiffs, turnId,
                parseUnifiedDiff(diff), transcript.workspacePath ?? undefined
              )
            }))
          }
        })
        const watch: TranscriptWatch = {
          users: 1,
          async loadEarlier() {
            if (!active || loadingEarlier || !turnCursor) return
            loadingEarlier = true
            try {
              await request('thread/turns/list', {
                threadId: childThreadId, cursor: turnCursor, sortDirection: 'descending', ...THREAD_OPENING_PAGE
              }, { onResponse(page) {
                if (!active) return
                turnCursor = page.nextCursor ?? null
                const older = [...(page.data ?? [])].reverse().map(turn =>
                  wireTurnToConversationTurn(turn as Record<string, unknown>))
                update(childThreadId, (transcript) => {
                  const known = new Set(transcript.turns.map(turn => turn.id))
                  return withTimeline(transcript, [
                    ...older.filter(turn => !known.has(turn.id)), ...transcript.turns
                  ], transcript.live)
                })
              } })
            } finally {
              loadingEarlier = false
            }
          },
          loadTurnItems(turnId, isNeeded, complete) {
            const pending = itemLoads.get(turnId)
            if (pending) return complete ? pending.then(() => {
              if (useTurnItemsLoadStore.getState().statuses[turnId] === 'failed') return
              return watch.loadTurnItems(turnId, isNeeded, true)
            }) : pending
            const current = (): boolean => active && isNeeded()
            if (!ready) return restoration.then(() => {
              if (ready && current()) return watch.loadTurnItems(turnId, isNeeded, complete)
            })
            const selected = get().transcripts.get(childThreadId)?.turns.find(turn => turn.id === turnId)
            if (!selected?.summary || !current()) return Promise.resolve()
            let cursor = selected.summary.nextCursor ?? null
            const loading = (async () => {
              useTurnItemsLoadStore.setState(({ statuses }) => ({ statuses: { ...statuses, [turnId]: 'loading' } }))
              do {
                if (!current()) return
                const page = await readTurnItemsPage(
                  (method, params, options) => request(method, params, options),
                  childThreadId, turnId, cursor, (page) => {
                    if (!current()) return
                    update(childThreadId, (transcript) => {
                      const turns = transcript.turns.map(turn => {
                        if (turn.id !== turnId) return turn
                        return mergeTurnItemsPage(turn, page.items.map(wireItemToConversationItem), page.nextCursor)
                      })
                      return withTimeline(transcript, turns, transcript.live)
                    })
                  }
                )
                cursor = page.nextCursor
              } while (complete && cursor !== null)
            })().catch(() => {
              if (current()) useTurnItemsLoadStore.setState(({ statuses }) => ({ statuses: { ...statuses, [turnId]: 'failed' } }))
            }).finally(() => {
              itemLoads.delete(turnId)
              const owner = watches.get(childThreadId)
              if (owner && owner !== watch) return
              if (useTurnItemsLoadStore.getState().statuses[turnId] !== 'failed' || !active) {
                useTurnItemsLoadStore.setState(({ statuses }) => {
                  const { [turnId]: _previous, ...rest } = statuses
                  return { statuses: rest }
                })
              }
            })
            itemLoads.set(turnId, loading)
            return loading
          },
          stop() {
            active = false
            ready = false
            clearTimeout(timer)
            deltas = []
            stopListening()
            release()
          }
        }
        watches.set(childThreadId, watch)
        restoration = (async () => {
          connectionKey = await window.api.appServer.getConnectionKey()
          if (!active) return
          if (!connectionKey) throw new Error('No transcript connection')
          const { thread } = await request('thread/read', { threadId: childThreadId })
          if (!active) return
          if (thread?.status === 'archived') {
            const page = await request('thread/turns/list', {
              threadId: childThreadId, sortDirection: 'descending', ...THREAD_OPENING_PAGE
            })
            if (!active) return
            turnCursor = page.nextCursor ?? null
            const turns = [...(page.data ?? [])].reverse().map(turn =>
              wireTurnToConversationTurn(turn as Record<string, unknown>))
            update(childThreadId, (transcript) => withTimeline({
              ...transcript, status: 'ready',
              configuration: thread.configuration as ThreadConfigurationWire ?? null,
              workspacePath: thread.workspacePath ?? null
            }, mergeRestoredTurns(transcript.turns, turns), null))
            ready = true
            return
          }
          await window.api.appServer.sendConnectionRequest(
            connectionKey, 'thread/resume', { threadId: childThreadId, initialTurnsPage: THREAD_OPENING_PAGE },
            undefined, {
              attachmentOwner,
              onResponse(result) {
                attached = true
                if (!active) {
                  release()
                  return
                }
                turnCursor = result.initialTurnsPage?.nextCursor ?? null
                update(childThreadId, (cached) => {
                  const timeline = restoreTranscript(result, cached.turns)
                  const transcript = withTimeline({
                    ...cached, status: 'ready',
                    configuration: result.thread.configuration as ThreadConfigurationWire ?? null,
                    workspacePath: result.thread.workspacePath ?? null
                  }, timeline.turns, timeline.live)
                  const { turnId, diff } = result.currentState
                  if (turnId && diff != null) {
                    transcript.turnDiffs = applyLiveTurnDiff(
                      transcript.turnDiffs, transcript.turns, transcript.itemDiffs, turnId,
                      parseUnifiedDiff(diff), transcript.workspacePath ?? undefined
                    )
                  }
                  return transcript
                })
                ready = true
              }
            }
          )
        })().catch(() => {
          if (active) update(childThreadId, (transcript) => ({ ...transcript, status: 'error' }))
        })
      }
      const watch = watches.get(childThreadId)!
      let watching = true
      return () => {
        if (!watching) return
        watching = false
        watch.users -= 1
        if (watch.users === 0) {
          watch.stop()
          if (watches.get(childThreadId) === watch) watches.delete(childThreadId)
        }
      }
    },

    async loadEarlier(childThreadId) {
      await watches.get(childThreadId)?.loadEarlier()
    },

    async loadTurnItems(childThreadId, turnId, isNeeded = () => true, complete = false) {
      await watches.get(childThreadId)?.loadTurnItems(turnId, isNeeded, complete)
    },

    setTurnFileStatus(childThreadId, turnId, key, status) {
      update(childThreadId, (transcript) => {
        const turnDiffs = setTurnFileStatus(transcript.turnDiffs, turnId, key, status)
        return turnDiffs === transcript.turnDiffs ? transcript : { ...transcript, turnDiffs }
      })
    },

    reset() {
      for (const watch of watches.values()) watch.stop()
      watches.clear()
      set({ transcripts: new Map() })
    }
  }
})

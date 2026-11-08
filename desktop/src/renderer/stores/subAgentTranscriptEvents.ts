import type { ClientRequestMethods } from '@dotcraft/sdk/contracts'
import { normalizeRemoteFileTransferProgress } from '../utils/remoteToolHostDisplay'
import { mergeRestoredTurns } from './turnItemsHydration'
import {
  wireItemToConversationItem,
  wireTurnToConversationTurn,
  type ConversationItem,
  type ConversationTurn
} from '../types/conversation'

export interface TranscriptLiveText {
  itemId: string
  kind: 'agentMessage' | 'reasoningContent'
  text: string
}

export interface TranscriptTimeline {
  turns: ConversationTurn[]
  live: TranscriptLiveText | null
}

export function restoreTranscript(
  result: ClientRequestMethods['thread/resume']['result'],
  existing: readonly ConversationTurn[] = []
): TranscriptTimeline {
  const current = result.currentState
  const incoming = [...(result.initialTurnsPage?.data ?? [])].reverse()
    .map(raw => wireTurnToConversationTurn(raw as Record<string, unknown>))
  const merged = mergeRestoredTurns(existing, incoming)
  const active = merged.find(turn => turn.id === current.turnId)
  const running = active?.status === 'running' || active?.status === 'waitingApproval' || active?.status === 'waitingInput'
  const item = running ? incoming.find(turn => turn.id === current.turnId)?.items.findLast(candidate =>
    candidate.status !== 'completed' && (candidate.type === 'agentMessage' || candidate.type === 'reasoningContent')
    && active?.items.find(item => item.id === candidate.id)?.status !== 'completed') : undefined
  const live: TranscriptLiveText | null = item ? {
    itemId: item.id,
    kind: item.type as TranscriptLiveText['kind'],
    text: item.type === 'agentMessage' ? item.text ?? '' : item.reasoning ?? ''
  } : null
  const turns = merged.map(turn => turn.id !== current.turnId ? turn : {
    ...turn,
    items: turn.items.map(item => ({
      ...item,
      ...(item.id === live?.itemId ? { status: 'streaming' as const } : {}),
      ...(current.toolArguments?.[item.id] != null ? { argumentsPreview: current.toolArguments[item.id] } : {}),
      ...(current.toolProgress?.[item.id] ? {
        transferProgress: normalizeRemoteFileTransferProgress(current.toolProgress[item.id].progress) ?? undefined
      } : {})
    }))
  })
  return { turns, live }
}

const TERMINAL_TURN_STATUS: Record<string, ConversationTurn['status']> = {
  'turn/completed': 'completed',
  'turn/failed': 'failed',
  'turn/cancelled': 'cancelled'
}

export function isTerminalTurnEvent(method: string): boolean {
  return method in TERMINAL_TURN_STATUS
}

export function transcriptEventThreadId(params: Record<string, unknown>): string | null {
  const turn = params.turn as Record<string, unknown> | undefined
  const threadId = params.threadId ?? turn?.threadId
  return typeof threadId === 'string' ? threadId : null
}

function byCreatedAt(items: ConversationItem[]): ConversationItem[] {
  return [...items].sort((a, b) => Date.parse(a.createdAt) - Date.parse(b.createdAt))
}

function withTurn(
  turns: ConversationTurn[],
  turnId: string,
  threadId: string,
  update: (turn: ConversationTurn) => ConversationTurn
): ConversationTurn[] {
  const index = turns.findIndex((turn) => turn.id === turnId)
  if (index >= 0) return turns.map((turn, position) => position === index ? update(turn) : turn)
  const shell: ConversationTurn = {
    id: turnId,
    threadId,
    status: 'running',
    items: [],
    startedAt: new Date().toISOString()
  }
  return [...turns, update(shell)]
}

function upsertItem(items: ConversationItem[], item: ConversationItem): ConversationItem[] {
  const existing = items.find((candidate) => candidate.id === item.id)
  if (!existing) return byCreatedAt([...items, item])
  if (existing.status === 'completed' && item.status !== 'completed') return items
  return items.map((candidate) => candidate.id === item.id ? { ...existing, ...item } : candidate)
}

export function applyTranscriptEvent(
  timeline: TranscriptTimeline,
  method: string,
  params: Record<string, unknown>
): TranscriptTimeline | null {
  const threadId = transcriptEventThreadId(params) ?? ''

  if (method === 'turn/started') {
    const turn = wireTurnToConversationTurn((params.turn ?? params) as Record<string, unknown>)
    return {
      ...timeline,
      turns: withTurn(timeline.turns, turn.id, threadId, (existing) => ({
        ...existing,
        status: 'running',
        startedAt: turn.startedAt
      }))
    }
  }

  const terminalStatus = TERMINAL_TURN_STATUS[method]
  if (terminalStatus) {
    const turn = wireTurnToConversationTurn((params.turn ?? params) as Record<string, unknown>)
    return {
      turns: withTurn(timeline.turns, turn.id, threadId, (existing) => ({
        ...existing,
        status: terminalStatus,
        completedAt: turn.completedAt ?? existing.completedAt,
        error: turn.error ?? existing.error,
        providerError: turn.providerError ?? existing.providerError
      })),
      live: null
    }
  }

  if (method === 'item/started' || method === 'item/completed') {
    const rawItem = params.item as Record<string, unknown> | undefined
    const turnId = params.turnId
    if (!rawItem || typeof turnId !== 'string') return null
    const mapped = wireItemToConversationItem(rawItem)
    const streamed = mapped.type === 'agentMessage' || mapped.type === 'reasoningContent'

    if (method === 'item/started') {
      const item: ConversationItem = streamed
        ? { ...mapped, status: 'streaming', text: '', reasoning: '' }
        : mapped
      return {
        turns: withTurn(timeline.turns, turnId, threadId, (turn) => ({
          ...turn,
          items: upsertItem(turn.items, item)
        })),
        live: streamed
          ? { itemId: mapped.id, kind: mapped.type as TranscriptLiveText['kind'], text: '' }
          : timeline.live
      }
    }

    const liveText = timeline.live?.itemId === mapped.id ? timeline.live.text : ''
    const completed: ConversationItem = {
      ...mapped,
      status: 'completed',
      ...(mapped.type === 'agentMessage' ? { text: mapped.text || liveText } : {}),
      ...(mapped.type === 'reasoningContent' ? { reasoning: mapped.reasoning || liveText } : {})
    }
    return {
      turns: withTurn(timeline.turns, turnId, threadId, (turn) => ({
        ...turn,
        items: upsertItem(turn.items, completed)
      })),
      live: timeline.live?.itemId === mapped.id ? null : timeline.live
    }
  }

  return null
}

export function appendLiveText(timeline: TranscriptTimeline, itemId: string, delta: string): TranscriptTimeline {
  if (timeline.live?.itemId !== itemId || delta.length === 0) return timeline
  return { ...timeline, live: { ...timeline.live, text: timeline.live.text + delta } }
}

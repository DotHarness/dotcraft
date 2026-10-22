import type { InputPart, SessionItem, SessionTurn } from '@dotcraft/sdk/contracts'

export interface HistoryItem {
  id: string
  turnId: string
  type: string
  status: string
  payload: Record<string, unknown>
  createdAt: string
  completedAt: string | null
  partial: boolean
}

export interface HistoryTurn {
  id: string
  status: string
  error: string | null
  providerError?: string | null
  startedAt?: string | null
  completedAt?: string | null
}

export interface Echo {
  clientId: string
  text: string
  parts?: InputPart[]
  added: boolean
}

export interface ChatHistory {
  items: HistoryItem[]
  turns: HistoryTurn[]
  echoes: Echo[]
  diffs?: Record<string, string>
  /** Cursor for the next page of older whole Turns; absent once the oldest Turn is loaded. */
  olderCursor?: string | null
}

/** One Turn read with every one of its Items, so a page never holds part of a Turn. */
export interface TurnPageEntry {
  turn: SessionTurn
  items: SessionItem[]
}

export type HistoryEvent =
  | { kind: 'item'; item: SessionItem }
  | { kind: 'delta'; itemId: string; turnId: string; itemType: 'agentMessage' | 'reasoningContent'; delta: string }
  | { kind: 'turn'; turn: SessionTurn }
  | { kind: 'diff'; turnId: string; diff: string }

export function emptyHistory(): ChatHistory {
  return { items: [], turns: [], echoes: [] }
}

export function isComplete(item: Pick<HistoryItem, 'status'>): boolean {
  return item.status === 'completed' || item.status === 'failed' || item.status === 'cancelled'
}

function textOf(item: HistoryItem): string {
  return (item.payload.text as string | undefined) ?? ''
}

function clientIdOf(item: HistoryItem): string | undefined {
  return item.type === 'userMessage' ? (item.payload.clientUserMessageId as string | undefined) : undefined
}

function itemFromWire(item: SessionItem): HistoryItem {
  return {
    id: item.id,
    turnId: item.turnId,
    type: item.type,
    status: item.status,
    payload: (item.payload ?? {}) as Record<string, unknown>,
    createdAt: item.createdAt,
    completedAt: item.completedAt ?? null,
    partial: false,
  }
}

function turnFromWire(turn: SessionTurn): HistoryTurn {
  return {
    id: turn.id,
    status: turn.status,
    error: turn.error ?? null,
    providerError: turn.providerError ?? null,
    startedAt: turn.startedAt,
    completedAt: turn.completedAt ?? null,
  }
}

function isStreamable(item: HistoryItem): boolean {
  return item.type === 'agentMessage' || item.type === 'reasoningContent'
}

function pageItem(item: SessionItem): HistoryItem {
  const entry = itemFromWire(item)
  return isStreamable(entry) && !isComplete(entry) ? { ...entry, partial: true } : entry
}

/** The newest page of whole Turns, oldest first. */
export function historyFromTurns(page: TurnPageEntry[], olderCursor: string | null): ChatHistory {
  return {
    items: page.flatMap((entry) => entry.items.map(pageItem)),
    turns: page.map((entry) => turnFromWire(entry.turn)),
    echoes: [],
    olderCursor,
  }
}

/** Puts a page of older whole Turns before the loaded ones, skipping any already present. */
export function prependTurns(history: ChatHistory, page: TurnPageEntry[], olderCursor: string | null): ChatHistory {
  const known = new Set(history.turns.map((turn) => turn.id))
  const older = page.filter((entry) => !known.has(entry.turn.id))
  return {
    ...history,
    items: [...older.flatMap((entry) => entry.items.map(pageItem)), ...history.items],
    turns: [...older.map((entry) => turnFromWire(entry.turn)), ...history.turns],
    olderCursor,
  }
}

function indexOf(history: ChatHistory, turnId: string, id: string): number {
  return history.items.findIndex((item) => item.turnId === turnId && item.id === id)
}

function upsertItem(history: ChatHistory, incoming: HistoryItem): ChatHistory {
  const clientId = clientIdOf(incoming)
  const echoes = clientId ? history.echoes.filter((echo) => echo.clientId !== clientId) : history.echoes
  const index = indexOf(history, incoming.turnId, incoming.id)
  if (index < 0) return { ...history, items: [...history.items, incoming], echoes }
  const existing = history.items[index]
  if (!isComplete(incoming) && isComplete(existing)) return { ...history, echoes }
  const next = isComplete(incoming)
    ? incoming
    : {
        ...incoming,
        partial: existing.partial,
        payload: { ...incoming.payload, text: textOf(existing).length > textOf(incoming).length ? textOf(existing) : textOf(incoming) },
      }
  const items = [...history.items]
  items[index] = next
  return { ...history, items, echoes }
}

function appendDelta(history: ChatHistory, delta: Extract<HistoryEvent, { kind: 'delta' }>): ChatHistory {
  const index = indexOf(history, delta.turnId, delta.itemId)
  if (index < 0) {
    const item: HistoryItem = {
      id: delta.itemId,
      turnId: delta.turnId,
      type: delta.itemType,
      status: 'started',
      payload: { text: '' },
      createdAt: '',
      completedAt: null,
      partial: true,
    }
    return { ...history, items: [...history.items, item] }
  }
  const existing = history.items[index]
  if (isComplete(existing) || existing.partial) return history
  const items = [...history.items]
  items[index] = { ...existing, payload: { ...existing.payload, text: textOf(existing) + delta.delta } }
  return { ...history, items }
}

function upsertTurn(history: ChatHistory, turn: HistoryTurn): ChatHistory {
  const index = history.turns.findIndex((entry) => entry.id === turn.id)
  const turns = [...history.turns]
  if (index < 0) turns.push(turn)
  else turns[index] = turn
  return { ...history, turns }
}

export function applyEvent(history: ChatHistory, event: HistoryEvent): ChatHistory {
  switch (event.kind) {
    case 'item':
      return upsertItem(history, itemFromWire(event.item))
    case 'delta':
      return appendDelta(history, event)
    case 'turn':
      return (event.turn.items ?? []).reduce(
        (next, item) => upsertItem(next, itemFromWire(item)),
        upsertTurn(history, turnFromWire(event.turn)),
      )
    case 'diff':
      return { ...history, diffs: { ...history.diffs, [event.turnId]: event.diff } }
  }
}

export function restoreEchoes(history: ChatHistory, echoes: Echo[]): ChatHistory {
  const acknowledged = new Set(history.items.map(clientIdOf))
  return { ...history, echoes: echoes.filter((echo) => !acknowledged.has(echo.clientId)) }
}

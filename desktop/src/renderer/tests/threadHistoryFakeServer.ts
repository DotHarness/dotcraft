import { vi } from 'vitest'
import { wireTurnToConversationTurn } from '../types/conversation'
import { useConversationStore } from '../stores/conversationStore'
import { useThreadStore } from '../stores/threadStore'
import { applyThreadHistoryHead, beginThreadHistory } from '../stores/threadHistoryStore'
import { readThreadHistoryHead } from '../utils/threadHistory'

export interface WireTurn {
  id: string
  threadId: string
  status: string
  startedAt: string
}

export type TurnItemsFactory = (turn: WireTurn) => Array<Record<string, unknown>>

function agentReplyOnly(turn: WireTurn): Array<Record<string, unknown>> {
  return [{ id: `${turn.id}-agent`, type: 'agentMessage', status: 'completed', text: `${turn.id} reply`, createdAt: turn.startedAt }]
}

export function userMessageAndReply(turn: WireTurn): Array<Record<string, unknown>> {
  return [
    { id: `${turn.id}-user`, type: 'userMessage', status: 'completed', text: `${turn.id} ask`, createdAt: turn.startedAt },
    ...agentReplyOnly(turn)
  ]
}

export interface FakeHistoryServer {
  request: ReturnType<typeof vi.fn>
  turnsListCalls(): Array<Record<string, unknown>>
  holdNextTurnsList(): () => void
}

/**
 * Serves `turn-1` … `turn-N` (oldest first) with an exclusive, direction-bound `nextCursor`
 * and an inclusive `backwardsCursor` anchored at the first returned Turn.
 */
export function createFakeHistoryServer(
  threadId: string,
  turnCount: number,
  itemsFor: TurnItemsFactory = agentReplyOnly
): FakeHistoryServer {
  const turns: WireTurn[] = Array.from({ length: turnCount }, (_unused, index) => ({
    id: `turn-${index + 1}`,
    threadId,
    status: 'completed',
    startedAt: new Date(Date.UTC(2026, 0, 1, 0, index)).toISOString()
  }))
  let hold: Promise<void> | null = null

  const listTurns = (params: Record<string, unknown>) => {
    const direction = params.sortDirection as 'ascending' | 'descending'
    const step = direction === 'descending' ? -1 : 1
    const cursor = params.cursor as string | null
    let start = step < 0 ? turns.length - 1 : 0
    if (cursor) {
      const [kind, ordinal, cursorDirection] = cursor.split(':')
      if (kind === 'next' && cursorDirection !== direction) throw new Error('InvalidParams')
      start = Number(ordinal) + (kind === 'next' ? step : 0)
    }
    const data: WireTurn[] = []
    for (let index = start; index >= 0 && index < turns.length && data.length < (params.limit as number); index += step) {
      data.push(turns[index])
    }
    const last = start + step * (data.length - 1)
    const more = data.length > 0 && last + step >= 0 && last + step < turns.length
    return {
      data,
      nextCursor: more ? `next:${last}:${direction}` : null,
      backwardsCursor: data.length > 0 ? `back:${start}` : null
    }
  }

  const request = vi.fn(async (method: string, params: Record<string, unknown>) => {
    if (method === 'thread/turns/list') {
      const held = hold
      hold = null
      if (held) await held
      return listTurns(params)
    }
    if (method === 'thread/items/list') {
      const turn = turns.find((candidate) => candidate.id === params.turnId)
      return {
        data: turn ? itemsFor(turn).map((item) => ({ turnId: turn.id, item })) : [],
        nextCursor: null
      }
    }
    if (method === 'thread/read') {
      return { thread: { id: threadId, displayName: null, status: 'active', turns: [] } }
    }
    return {}
  })

  return {
    request,
    turnsListCalls: () => request.mock.calls
      .filter(([method]) => method === 'thread/turns/list')
      .map(([, params]) => params as Record<string, unknown>),
    holdNextTurnsList: () => {
      let release!: () => void
      hold = new Promise<void>((resolve) => { release = resolve })
      return release
    }
  }
}

export async function openThreadHistory(server: FakeHistoryServer, threadId: string): Promise<void> {
  useThreadStore.setState({ activeThreadId: threadId })
  beginThreadHistory(threadId)
  const head = await readThreadHistoryHead(server.request, threadId)
  const turns = head.thread.turns ?? []
  applyThreadHistoryHead(threadId, turns, head.turnCursor)
  useConversationStore.getState().setTurns(
    turns.map((turn) => wireTurnToConversationTurn(turn as unknown as Record<string, unknown>)),
    { preserveExistingRealtime: true, realtimeScopeThreadId: threadId }
  )
}

export function loadedTurnIds(): string[] {
  return useConversationStore.getState().turns.map((turn) => turn.id)
}

export function turnRange(first: number, last: number): string[] {
  return Array.from({ length: last - first + 1 }, (_unused, index) => `turn-${first + index}`)
}

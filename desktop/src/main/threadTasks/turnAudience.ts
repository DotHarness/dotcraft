import type { ThreadTaskConnection } from './childThreads'

const DESKTOP_SURFACE = 'dotcraft-desktop'
const PHONE_SURFACE = 'dotcraft-mobile'
const MAX_REMEMBERED_TURNS = 200

type JsonObject = Record<string, unknown>

export function turnCameFromComposer(turn: JsonObject, firstItem: unknown): boolean {
  if (turn.originChannel !== DESKTOP_SURFACE) return false
  if (!isRecord(firstItem) || firstItem.type !== 'userMessage') return false
  const payload = isRecord(firstItem.payload) ? firstItem.payload : {}
  return typeof payload.triggerKind !== 'string' || payload.triggerKind.trim() === ''
}

interface TurnOrigin {
  composer: boolean
  phone: boolean
}

function originOf(turn: JsonObject, firstItem: unknown): TurnOrigin {
  return { composer: turnCameFromComposer(turn, firstItem), phone: turn.originChannel === PHONE_SURFACE }
}

export class TurnAudienceTracker {
  private readonly turns = new Map<string, TurnOrigin>()

  observe(method: string, params: unknown): void {
    if (method !== 'turn/started' || !isRecord(params) || !isRecord(params.turn)) return
    const turn = params.turn
    const threadId = typeof turn.threadId === 'string' ? turn.threadId : params.threadId
    if (typeof threadId !== 'string' || typeof turn.id !== 'string') return
    const items = Array.isArray(turn.items) ? turn.items : []
    this.remember(threadId, turn.id, originOf(turn, items[0]))
  }

  async fromComposer(connection: ThreadTaskConnection, threadId: string, turnId: string | undefined): Promise<boolean> {
    return (await this.origin(connection, threadId, turnId))?.composer === true
  }

  async fromPhone(connection: ThreadTaskConnection, threadId: string, turnId: string | undefined): Promise<boolean> {
    return (await this.origin(connection, threadId, turnId))?.phone === true
  }

  private async origin(connection: ThreadTaskConnection, threadId: string, turnId: string | undefined): Promise<TurnOrigin | null> {
    if (!turnId) return null
    const known = this.turns.get(key(threadId, turnId))
    if (known !== undefined) return known
    try {
      const [turns, items] = await Promise.all([
        connection.request<{ data?: unknown[] }>('thread/turns/list', { threadId, limit: 5 }),
        connection.request<{ data?: unknown[] }>('thread/items/list', {
          threadId,
          turnId,
          limit: 1,
          sortDirection: 'ascending'
        })
      ])
      const turn = (turns.data ?? []).find((candidate) => isRecord(candidate) && candidate.id === turnId)
      const first = (items.data ?? [])[0]
      if (!isRecord(turn)) return null
      const result = originOf(turn, isRecord(first) ? first.item : undefined)
      this.remember(threadId, turnId, result)
      return result
    } catch {
      return null
    }
  }

  private remember(threadId: string, turnId: string, origin: TurnOrigin): void {
    this.turns.set(key(threadId, turnId), origin)
    for (const oldest of this.turns.keys()) {
      if (this.turns.size <= MAX_REMEMBERED_TURNS) break
      this.turns.delete(oldest)
    }
  }
}

function key(threadId: string, turnId: string): string {
  return `${threadId}\n${turnId}`
}

function isRecord(value: unknown): value is JsonObject {
  return value != null && typeof value === 'object' && !Array.isArray(value)
}

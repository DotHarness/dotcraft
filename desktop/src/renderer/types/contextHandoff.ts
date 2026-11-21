import type { ConversationTurn } from './conversation'

export interface HandoffIdentity {
  providerId: string
  model: string
}

export interface ContextHandoff {
  from: HandoffIdentity
  to: HandoffIdentity
}

function readIdentity(value: unknown): HandoffIdentity | undefined {
  if (value == null || typeof value !== 'object') return undefined
  const { providerId, model } = value as Record<string, unknown>
  if (typeof providerId !== 'string' || !providerId.trim() ||
      typeof model !== 'string' || !model.trim()) return undefined
  return { providerId, model }
}

export function readContextHandoff(source: unknown, target: unknown): ContextHandoff | undefined {
  const from = readIdentity(source)
  const to = readIdentity(target)
  return from && to ? { from, to } : undefined
}

export function getTurnContextHandoff(turn: ConversationTurn): ContextHandoff | undefined {
  return turn.items.find((item) => item.systemNotice?.contextHandoff)?.systemNotice?.contextHandoff
}

import type { ChatHistory, HistoryItem, HistoryTurn } from './history'
import type { OriginMark, ThreadAction, TranscriptEntry } from './transcript'

const CHANNEL_LABELS: Record<string, string> = {
  qq: 'QQ',
  wecom: 'WeCom',
  weixin: 'WeChat',
  wechat: 'WeChat',
  telegram: 'Telegram',
  feishu: 'Feishu',
}

const ORIGINS: Record<string, OriginMark['kind']> = {
  workflow: 'workflow',
  automation: 'automation',
  app: 'app',
  goal: 'goal',
  subagentFollowupTask: 'subagent',
  subagentInput: 'subagent',
}

function str(value: unknown): string {
  return typeof value === 'string' ? value : ''
}

function idOf(item: HistoryItem): string {
  return `${item.turnId}/${item.id}`
}

function channelLabel(name: string): string {
  return CHANNEL_LABELS[name.trim().toLowerCase()] ?? name.trim()
}

export function originOf(payload: Record<string, unknown>): OriginMark | null {
  const kind = ORIGINS[str(payload.triggerKind)]
  return kind ? { kind, label: str(payload.triggerLabel).trim(), threadId: null } : null
}

export function dividerOf(item: HistoryItem): Extract<TranscriptEntry, { kind: 'divider' }> | null {
  const payload = item.payload
  const id = idOf(item)
  const reason = str(payload.reason)
  switch (str(payload.kind)) {
    case 'forked':
      return { kind: 'divider', id, divider: 'forked' }
    case 'remoteRoute': {
      const host = str(payload.hostName) || str(payload.hostId)
      if (reason === 'connected') return { kind: 'divider', id, divider: 'remoteConnected', host }
      if (reason === 'disconnected' && str(payload.initiator) === 'client') return { kind: 'divider', id, divider: 'remoteLocal' }
      if (reason === 'disconnected' || reason === 'leaseLost') return { kind: 'divider', id, divider: 'remoteDisconnected', host }
      return null
    }
    case 'channel': {
      const channel = str(payload.channelName)
      if (!channel) return null
      if (reason === 'bound') return { kind: 'divider', id, divider: 'channelBound', channel: channelLabel(channel), target: str(payload.targetName) }
      if (reason === 'unbound') return { kind: 'divider', id, divider: 'channelUnbound', channel: channelLabel(channel) }
      return null
    }
    case 'compacted': {
      if (str(payload.mode) === 'micro') return null
      const trigger = str(payload.trigger)
      return { kind: 'divider', id, divider: trigger === 'auto' ? 'compactedAuto' : trigger === 'manual' ? 'compactedManual' : 'compactedReactive' }
    }
    default:
      return null
  }
}

const OVERLOADED = new Set(['serverOverloaded', 'rateLimitExceeded'])
const CAPACITY_RETRY_DELAYS = [10, 30, 120, 300]

export function failureEntry(id: string, message: string, providerError: string | null | undefined): TranscriptEntry {
  if (providerError === 'usageLimitExceeded') return { kind: 'divider', id, divider: 'usageLimit' }
  return { kind: 'failure', id, message, overloaded: OVERLOADED.has(providerError ?? ''), retry: null }
}

function isCapacityFailure(turn: HistoryTurn): boolean {
  return turn.status === 'failed' && turn.providerError === 'serverOverloaded'
}

export function capacityRetry(history: ChatHistory): { turnId: string; delay: number | null } | null {
  const turns = history.turns
  const last = turns.at(-1)
  if (!last || !isCapacityFailure(last)) return null
  const reissued = (turn: HistoryTurn) => !history.items.some((item) => item.turnId === turn.id && item.type === 'userMessage')
  let consecutive = 0
  for (let index = turns.length - 1; index > 0 && consecutive < CAPACITY_RETRY_DELAYS.length; index--) {
    if (!isCapacityFailure(turns[index]) || !reissued(turns[index]) || !isCapacityFailure(turns[index - 1])) break
    consecutive += 1
  }
  return { turnId: last.id, delay: CAPACITY_RETRY_DELAYS[consecutive] ?? null }
}

export function supersededTurns(history: ChatHistory): Set<string> {
  const bare = (turn: HistoryTurn) =>
    turn.status === 'failed' && history.items.every((item) => item.turnId !== turn.id || item.type === 'error')
  const superseded = new Set<string>()
  for (let index = 0; index < history.turns.length - 1; index++) {
    const turn = history.turns[index]
    const next = history.turns[index + 1]
    if (bare(turn) && bare(next) && (turn.providerError ?? turn.error) === (next.providerError ?? next.error)) superseded.add(turn.id)
  }
  return superseded
}

function structuredOf(result: HistoryItem): Record<string, unknown> | null {
  const structured = result.payload.structuredResult ?? result.payload.structuredContent
  return structured && typeof structured === 'object' ? (structured as Record<string, unknown>) : null
}

export function threadActionOf(item: HistoryItem, result: HistoryItem | undefined): ThreadAction | null {
  const toolName = str(item.payload.toolName)
  const namespace = str(item.payload.namespace)
  if ((toolName !== 'CreateThread' && toolName !== 'SendMessageToThread') || (namespace && namespace !== 'desktop')) return null
  if (!result || result.payload.success !== true) return null
  const data = structuredOf(result)
  if (!data) return null
  if (toolName === 'CreateThread') {
    const thread = data.thread as Record<string, unknown> | undefined
    const threadId = str(thread?.id)
    return threadId ? { kind: 'created', threadId, name: str(thread?.displayName).trim() || null, queued: false } : null
  }
  const threadId = str(data.threadId)
  return threadId ? { kind: 'messaged', threadId, name: null, queued: data.queued === true } : null
}
